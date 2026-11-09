using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using System.Net;

namespace NyriaNest.Application.Services
{
    public interface IUserService
    {
        // callerId / callerIsSuperAdmin = the admin asking. The rules are in AdminMayNotUpdate/-Delete.
        Task<APIResponse<UserDto>> DeleteUserAsync(bool callerIsSuperAdmin, string userId);
        Task<APIResponse<UserDto>> UpdateUserAsAdminAsync(string callerId, bool callerIsSuperAdmin, string userId, UpdateUserAsAdminDto userToUpdate);
        Task<APIResponse<UserDto>> GetUserByIdAsync(string userId);
        Task<APIResponse<List<UserDto>>> GetAllUsersAsync();
        Task<APIResponse<CreateAdminUserDto>> CreateAdminUserAsync(CreateAdminUserDto createAdminUserDto);
        Task<APIResponse<List<StatusHistoryItem>>> GetUserHistoryAsync(string userId);
    }

    public class UserService : IUserService
    {
        private readonly IUserRepo _userRepo;
        private readonly IBookRepo _bookRepo;
        private readonly IMapper _mapper;
        private readonly IBookService _bookService;
        private readonly IAppDbContext _dbContext;
        private readonly UserManager<User> _userManager;
        private readonly IEmailService _emailService;
        public UserService(IUserRepo userRepo, IBookRepo bookRepo, IMapper mapper, IBookService bookService, IAppDbContext dbContext, UserManager<User> userManager, IEmailService emailService)
        {
            _userRepo = userRepo;
            _bookRepo = bookRepo;
            _mapper = mapper;
            _bookService = bookService;
            _dbContext = dbContext;
            _userManager = userManager;
            _emailService = emailService;
        }

        public async Task<APIResponse<CreateAdminUserDto>> CreateAdminUserAsync(CreateAdminUserDto createAdminUserDto)
        {
            var response = new APIResponse<CreateAdminUserDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            if (!Roles.AllRoles.Contains(createAdminUserDto.Role))
            {
                response.ErrorMessages.Add("Invalid AdminRole.");
                return response;

            }
            CheckIfExists(createAdminUserDto.Email, createAdminUserDto.UserName);
            var user = _mapper.Map<User>(createAdminUserDto);

            // Create user with password
            var result = await _userManager.CreateAsync(user, createAdminUserDto.Password);
            if (!result.Succeeded)
            {
                response.ErrorMessages.AddRange(result.Errors.Select(e => e.Description));
                return response;
            }

            // Assign admin role
            var roleResult = await _userManager.AddToRoleAsync(user, createAdminUserDto.Role);
            if (!roleResult.Succeeded)
            {
                response.ErrorMessages.AddRange(roleResult.Errors.Select(e => e.Description));
                return response;
            }

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.Created;
            response.Result = createAdminUserDto;
            return response;
        }

        private void CheckIfExists(string email, string userName)
        {
            var user = _userRepo.GetByEmailAsync<User>(email).Result;
            if (user != null)
            {
                throw new Exception("Email already exists.");
            }
            var userNameExists = _userRepo.GetByUserNameAsync<User>(userName).Result;
            if (userNameExists != null)
            {
                throw new Exception("UserName already exists.");
            }
        }
        public async Task<APIResponse<UserDto>> DeleteUserAsync(bool callerIsSuperAdmin, string userId)
        {
            var response = new APIResponse<UserDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            // Find the user using UserManager (by Id)
            var user = await _userManager.Users
                .Include(u => u.Reviews)
                .Include(u => u.CheckedOutBooks)
                .Include(u => u.ReservedBooks)
                .Include(u => u.UserHistory)
                .FirstOrDefaultAsync(u => u.Id == userId);

            var refusal = user == null ? null
                : AdminMayNotDelete(callerIsSuperAdmin, await _userManager.GetRolesAsync(user));
            if (refusal != null)
            {
                response.ErrorMessages.Add(refusal);
                response.StatusCode = HttpStatusCode.Forbidden;
            }
            else if (user != null)
            {
                // Remove all ReservationItems for this user
                var reservations = await _dbContext.ReservationItems.Where(r => r.UserId == userId).ToListAsync();
                _dbContext.ReservationItems.RemoveRange(reservations);

                // Remove all CheckedOutItems for this user
                var checkedOuts = await _dbContext.CheckOutItems.Where(c => c.UserId == userId).ToListAsync();
                _dbContext.CheckOutItems.RemoveRange(checkedOuts);

                // Remove all ReviewItems for this user
                var reviews = await _dbContext.ReviewItems.Where(r => r.UserId == userId).ToListAsync();
                _dbContext.ReviewItems.RemoveRange(reviews);

                // Remove all StatusHistoryItems for this user
                var statusHistory = await _dbContext.StatusHistoryItems.Where(s => s.UserId == userId).ToListAsync();
                _dbContext.StatusHistoryItems.RemoveRange(statusHistory);

                // Remove from Book lists (if needed, EF should handle this if relationships are set up with cascade delete)
                // But to be explicit, update each affected book:
                var books = await _dbContext.Books
                    .Include(b => b.Reservations)
                    .Include(b => b.CheckedOutBy)
                    .Include(b => b.Reviews)
                    .Include(b => b.StatusHistory)
                    .ToListAsync();

                foreach (var book in books)
                {
                    // Remove user-related items
                    book.Reservations.RemoveAll(r => r.UserId == userId);
                    if (book.CheckedOutBy != null && book.CheckedOutBy.UserId == userId)
                        book.CheckedOutBy = null;
                    book.Reviews.RemoveAll(r => r.UserId == userId);
                    book.StatusHistory.RemoveAll(s => s.UserId == userId);

                    // Always update status after user-related items are removed
                    await _bookService.UpdateBookStatusAsync(book, null, book.BookStatus, "Updated after user deletion");
                }

                await _dbContext.SaveChangesAsync();

                // Now delete the user
                var result = await _userManager.DeleteAsync(user);
                if (!result.Succeeded)
                {
                    response.ErrorMessages.AddRange(result.Errors.Select(e => e.Description));
                    response.StatusCode = HttpStatusCode.InternalServerError;
                    return response;
                }

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = _mapper.Map<UserDto>(user);
            }
            else
            {
                response.ErrorMessages.Add("User not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }
            return response;
        }

        // What an Admin or SuperAdmin may do to another user (finding 4, #48).
        // Returns why not, or null when it's allowed.
        //   Admin:      only regular users' details. No passwords, no roles.
        //   SuperAdmin: anyone's details and password and any role, except another
        //               SuperAdmin, and never their own role (so nobody locks themselves out).
        private static string? AdminMayNotUpdate(string callerId, bool callerIsSuperAdmin,
            string targetId, IList<string> targetRoles, bool changesRole, bool setsPassword)
        {
            const string NoPermission = "You do not have permission to change this user.";
            var targetIsSuperAdmin = targetRoles.Contains(Roles.SuperAdmin);
            var targetIsAdmin = targetRoles.Contains(Roles.Admin);

            if (callerIsSuperAdmin)
            {
                if (targetIsSuperAdmin && targetId != callerId) return NoPermission;
                if (changesRole && targetId == callerId) return "You can't change your own role.";
                return null;
            }
            if (targetIsSuperAdmin || targetIsAdmin) return NoPermission;
            if (changesRole) return "Only a SuperAdmin can change roles.";
            if (setsPassword) return "Only a SuperAdmin can set another user's password.";
            return null;
        }

        // Admin: regular users only. SuperAdmin: anyone except a SuperAdmin, themselves included.
        private static string? AdminMayNotDelete(bool callerIsSuperAdmin, IList<string> targetRoles)
        {
            const string NoPermission = "You do not have permission to delete this user.";
            if (targetRoles.Contains(Roles.SuperAdmin)) return NoPermission;
            if (targetRoles.Contains(Roles.Admin) && !callerIsSuperAdmin) return NoPermission;
            return null;
        }

        public async Task<APIResponse<UserDto>> UpdateUserAsAdminAsync(string callerId, bool callerIsSuperAdmin, string userId, UpdateUserAsAdminDto userToUpdate)
        {
            var response = new APIResponse<UserDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var user = await _userRepo.GetByIdAsync<User>(userId);
            if (user != null)
            {
                // Decide before anything changes. The Manage users page sends the whole
                // user, so a role equal to the current one is not a role change.
                var targetRoles = await _userManager.GetRolesAsync(user);
                var changesRole = !string.IsNullOrWhiteSpace(userToUpdate.Role)
                    && !targetRoles.Contains(userToUpdate.Role);
                var setsPassword = !string.IsNullOrWhiteSpace(userToUpdate.Password);

                if (changesRole && !Roles.AllRoles.Contains(userToUpdate.Role!))
                {
                    response.ErrorMessages.Add("Unknown role.");
                    return response;
                }
                var refusal = AdminMayNotUpdate(callerId, callerIsSuperAdmin, user.Id, targetRoles, changesRole, setsPassword);
                if (refusal != null)
                {
                    response.ErrorMessages.Add(refusal);
                    response.StatusCode = HttpStatusCode.Forbidden;
                    return response;
                }

                // Map UpdateUserAsAdminDto to User
                _mapper.Map(userToUpdate, user);
                // If password is being changed, use UserManager
                if (!string.IsNullOrWhiteSpace(userToUpdate.Password))
                {
                    // Remove old password and set new one
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var passwordResult = await _userManager.ResetPasswordAsync(user, token, userToUpdate.Password);
                    if (!passwordResult.Succeeded)
                    {
                        response.ErrorMessages.AddRange(passwordResult.Errors.Select(e => e.Description));
                        return response;
                    }
                }
                if (changesRole)
                {
                    var currentRoles = targetRoles;
                    // Remove all current roles
                    if (currentRoles.Any())
                    {
                        var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                        if (!removeResult.Succeeded)
                        {
                            response.ErrorMessages.AddRange(removeResult.Errors.Select(e => e.Description));
                            return response;
                        }
                    }
                    // Add new role
                    var addResult = await _userManager.AddToRoleAsync(user, userToUpdate.Role);
                    if (!addResult.Succeeded)
                    {
                        response.ErrorMessages.AddRange(addResult.Errors.Select(e => e.Description));
                        return response;
                    }
                    // Update the User.Role property
                    user.Role = userToUpdate.Role;
                }
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    response.ErrorMessages.AddRange(updateResult.Errors.Select(e => e.Description));
                    return response;
                }

                // The same shape as GET api/User returns, without Identity internals
                var updatedUserDto = _mapper.Map<UserDto>(user);
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = updatedUserDto;
            }
            else
            {
                response.ErrorMessages.Add("User not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }
            return response;
        }
        public async Task<APIResponse<UserDto>> GetUserByIdAsync(string userId)
        {
            var response = new APIResponse<UserDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var user = await _userRepo.GetByIdAsync<User>(userId);
            if (user != null)
            {
                var userDto = _mapper.Map<UserDto>(user);
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = userDto;
            }
            else
            {
                response.ErrorMessages.Add("User not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }
            return response;
        }
        // DTOs, never the Identity users themselves: those carry PasswordHash and SecurityStamp
        public async Task<APIResponse<List<UserDto>>> GetAllUsersAsync()
        {
            var response = new APIResponse<List<UserDto>>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            try
            {
                var users = await _userRepo.GetAllAsync<User>();
                if (users != null)
                {
                    response.Result = _mapper.Map<List<UserDto>>(users);
                    response.IsSuccess = true;
                    response.StatusCode = HttpStatusCode.OK;
                }
                else
                {
                    response.ErrorMessages.Add("No users found.");
                    response.StatusCode = HttpStatusCode.NotFound;
                }
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.ErrorMessages.Add(ex.Message);
                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }

        public async Task<APIResponse<List<StatusHistoryItem>>> GetUserHistoryAsync(string userId)
        {
            var response = new APIResponse<List<StatusHistoryItem>>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var user = await _userRepo.GetByIdAsync<User>(userId);
            if (user != null)
            {
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = user.UserHistory
                    .OrderByDescending(r => r.Timestamp)
                    .ToList();
            }
            else
            {
                response.ErrorMessages.Add("User not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }
            return response;
        }


    }
}


