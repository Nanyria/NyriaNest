using AutoMapper;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using System.Net;

namespace NyriaNest.Application.ReadingList
{
    public interface IReadingListService
    {
        Task<APIResponse<List<FavoriteItemDto>>> GetFavoriteItemsAsync(string userId);
        Task<APIResponse<FavoriteItemDto>> AddToFavoritesAsync(string userId, int bookId);
        Task<APIResponse<FavoriteItemDto>> RemoveFromFavoritesAsync(string userId, int bookId);
    }

    /// <summary>
    /// The user's reading list (stored as FavoriteItems): list, add and remove books.
    /// Moved out of UserActionsService unchanged (#116).
    /// </summary>
    public class ReadingListService : IReadingListService
    {
        private readonly IUserRepo _userRepo;
        private readonly IBookRepo _bookRepo;
        private readonly IAppDbContext _dbContext;
        private readonly IMapper _mapper;

        public ReadingListService(IUserRepo userRepo, IBookRepo bookRepo, IAppDbContext dbContext, IMapper mapper)
        {
            _userRepo = userRepo;
            _bookRepo = bookRepo;
            _dbContext = dbContext;
            _mapper = mapper;
        }

        public async Task<APIResponse<List<FavoriteItemDto>>> GetFavoriteItemsAsync(string userId)
        {
            var response = new APIResponse<List<FavoriteItemDto>>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var user = await _userRepo.GetByIdAsync<User>(userId);
            if (user == null)
            {
                response.ErrorMessages.Add("User not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }
            if (user.ReadList == null || !user.ReadList.Any())
            {
                response.ErrorMessages.Add("No favorites found for this user.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            var favoriteDtos = _mapper.Map<List<FavoriteItemDto>>(user.ReadList);
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = favoriteDtos;
            return response;
        }
        public async Task<APIResponse<FavoriteItemDto>> AddToFavoritesAsync(string userId, int bookId)
        {
            var response = new APIResponse<FavoriteItemDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            var user = await _userRepo.GetByIdAsync<User>(userId);
            var book = await _bookRepo.GetByIdAsync(bookId);

            if (user == null || book == null)
            {
                response.ErrorMessages.Add("User or Book not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            // Prevent duplicates
            if (user.ReadList.Any(f => f.BookId == bookId))
            {
                response.ErrorMessages.Add("Book already in favorites.");
                response.StatusCode = HttpStatusCode.Conflict;
                return response;
            }

            var newFavorite = new FavoriteItem
            {
                BookId = book.BookId,
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.FavoriteItems.Add(newFavorite);
            user.ReadList.Add(newFavorite);
            await _dbContext.SaveChangesAsync();

            var resultDto = _mapper.Map<FavoriteItemDto>(newFavorite);
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.Created;
            response.Result = resultDto;
            return response;
        }
        public async Task<APIResponse<FavoriteItemDto>> RemoveFromFavoritesAsync(string userId, int bookId)
        {
            var response = new APIResponse<FavoriteItemDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            var favorite = await _dbContext.FavoriteItems
                .FirstOrDefaultAsync(f => f.UserId == userId && f.BookId == bookId);

            if (favorite == null)
            {
                response.ErrorMessages.Add("Favorite item not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            _dbContext.FavoriteItems.Remove(favorite);
            await _dbContext.SaveChangesAsync();

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = _mapper.Map<FavoriteItemDto>(favorite);
            return response;
        }
    }
}
