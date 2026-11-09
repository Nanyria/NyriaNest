using AutoMapper;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using System.Net;

namespace NyriaNest.Application.Reviews
{
    public interface IReviewService
    {
        Task<APIResponse<List<BookReviewDto>>> GetBookReviewsAsync(int bookId);
        Task<APIResponse<List<ReviewItemDto>>> GetUserReviewsAsync(string userId);
        Task<APIResponse<CreateReviewItemDto>> CreateReviewAsync(CreateReviewItemDto reviewItemDto);
        // userId = who is asking. Only the review's author may change or delete it,
        // except isModerator: true lets any review or rating be deleted. Only admin
        // endpoints pass it (#50); UserActionsController never does.
        Task<APIResponse<ReviewItemDto>> EditReviewAsync(string userId, int reviewId, ReviewItemDto reviewItemDto);
        Task<APIResponse<ReviewItemDto>> RemoveReviewItemAsync(string userId, int reviewId, bool isModerator = false);
        Task<APIResponse<RatingItemDto>> EditRatingAsync(string userId, int reviewId, RatingItemDto ratingItemDto);
        Task<APIResponse<RatingItemDto>> RemoveRatingAsync(string userId, int reviewId, bool isModerator = false);
    }

    /// <summary>
    /// Reviews and their ratings: a book's reviews, the user's own reviews, and
    /// creating, editing and removing them. Moved out of UserActionsService and
    /// BookService unchanged (#116).
    /// </summary>
    public class ReviewService : IReviewService
    {
        private readonly IUserRepo _userRepo;
        private readonly IBookRepo _bookRepo;
        private readonly IAppDbContext _dbContext;
        private readonly IMapper _mapper;

        public ReviewService(IUserRepo userRepo, IBookRepo bookRepo, IAppDbContext dbContext, IMapper mapper)
        {
            _userRepo = userRepo;
            _bookRepo = bookRepo;
            _dbContext = dbContext;
            _mapper = mapper;
        }

        public async Task<APIResponse<List<BookReviewDto>>> GetBookReviewsAsync(int bookId)
        {
            var response = new APIResponse<List<BookReviewDto>>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            var book = await _bookRepo.GetByIdAsync(bookId);
            if (book != null)
            {
                // Use AutoMapper to map ReviewItem to BookReviewDto
                response.Result = _mapper.Map<List<BookReviewDto>>(book.Reviews.OrderByDescending(r => r.CreatedAt).ToList());
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
            }
            else
            {
                response.ErrorMessages.Add("Book not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }
            return response;
        }

        public async Task<APIResponse<List<ReviewItemDto>>> GetUserReviewsAsync(string userId)
        {
            var response = new APIResponse<List<ReviewItemDto>>
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
            // Read straight from the reviews: the user as loaded above doesn't include them,
            // so this list always looked empty (#117)
            var reviews = await _dbContext.ReviewItems
                .Include(r => r.Book)
                .Include(r => r.RatingItem)
                .Where(r => r.UserId == userId)
                .ToListAsync();
            if (!reviews.Any())
            {
                response.ErrorMessages.Add("No reviews found for this user.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            var reviewDtos = _mapper.Map<List<ReviewItemDto>>(reviews);
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = reviewDtos;
            return response;
        }
        public async Task<APIResponse<CreateReviewItemDto>> CreateReviewAsync(CreateReviewItemDto reviewItemDto)
        {
            var response = new APIResponse<CreateReviewItemDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var user = await _userRepo.GetByIdAsync<User>(reviewItemDto.UserId);
            var book = await _bookRepo.GetByIdAsync(reviewItemDto.BookId);
            // Without these checks an unknown book crashed with a 500 (#117)
            if (user == null || book == null)
            {
                response.ErrorMessages.Add(book == null ? "Book not found." : "User not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }
            var newReview = new ReviewItem
            {
                BookId = book.BookId,
                UserId = user.Id,
                CreatedAt = reviewItemDto.CreatedAt,
                ReviewHeader = reviewItemDto.ReviewHeader,
                ReviewText = reviewItemDto.ReviewText
            };
            // Add review to context and save to get Id
            _dbContext.ReviewItems.Add(newReview);
            user.Reviews.Add(newReview);
            book.Reviews.Add(newReview);
            await _dbContext.SaveChangesAsync();

            // If a rating is provided, create RatingItem and link it
            if (reviewItemDto.RatingItem != null)
            {
                var rating = _mapper.Map<RatingItem>(reviewItemDto.RatingItem);
                rating.ReviewItemId = newReview.Id; // Set the FK after review is saved
                _dbContext.RatingItems.Add(rating);
                await _dbContext.SaveChangesAsync();

                newReview.RatingItem = rating;
            }
            // Map back to DTO for response
            var resultDto = _mapper.Map<CreateReviewItemDto>(newReview);
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.Created;
            response.Result = resultDto;
            return response;
        }
        public async Task<APIResponse<ReviewItemDto>> EditReviewAsync(string userId, int reviewId, ReviewItemDto reviewItemDto)
        {
            var response = new APIResponse<ReviewItemDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var reviewToEdit = await _dbContext.ReviewItems.FindAsync(reviewId);

            if (reviewToEdit != null && reviewToEdit.UserId != userId)
            {
                SetNoReviewPermission(response);
            }
            else if (reviewToEdit != null)
            {
                reviewToEdit.ReviewHeader = reviewItemDto.ReviewHeader;
                reviewToEdit.ReviewText = reviewItemDto.ReviewText;
                await _dbContext.SaveChangesAsync();
                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = _mapper.Map<ReviewItemDto>(reviewToEdit);
            }
            else
            {
                response.ErrorMessages.Add("Review item not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }
            return response;
        }
        public async Task<APIResponse<ReviewItemDto>> RemoveReviewItemAsync(string userId, int reviewId, bool isModerator = false)
        {
            var response = new APIResponse<ReviewItemDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var reviewToDelete = await _dbContext.ReviewItems
                .Include(r => r.User)
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.Id == reviewId);
            if (reviewToDelete != null && reviewToDelete.UserId != userId && !isModerator)
            {
                SetNoReviewPermission(response);
            }
            else if (reviewToDelete != null)
            {
                // Remove from User's Reviews
                if (reviewToDelete.User != null)
                    reviewToDelete.User.Reviews.Remove(reviewToDelete);

                // Remove from Book's Reviews
                if (reviewToDelete.Book != null)
                    reviewToDelete.Book.Reviews.Remove(reviewToDelete);

                // Remove from DbContext
                _dbContext.ReviewItems.Remove(reviewToDelete);

                await _dbContext.SaveChangesAsync();

                response.IsSuccess = true;
                response.StatusCode = HttpStatusCode.OK;
                response.Result = _mapper.Map<ReviewItemDto>(reviewToDelete);
            }
            else
            {
                response.ErrorMessages.Add("Review item not found.");
                response.StatusCode = HttpStatusCode.NotFound;
            }
            return response;
        }
        public async Task<APIResponse<RatingItemDto>> EditRatingAsync(string userId, int reviewId, RatingItemDto ratingItemDto)
        {
            var response = new APIResponse<RatingItemDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };

            // Find the review by ReviewItemId
            var reviewToEdit = await _dbContext.ReviewItems
                .Include(r => r.RatingItem)
                .FirstOrDefaultAsync(r => r.Id == reviewId);

            if (reviewToEdit == null || reviewToEdit.RatingItem == null)
            {
                response.ErrorMessages.Add("Review or Rating not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }
            if (reviewToEdit.UserId != userId)
            {
                SetNoReviewPermission(response);
                return response;
            }

            // Validate the rating range (optional, for immediate feedback)
            if (ratingItemDto.Rating < 0 || ratingItemDto.Rating > 10)
            {
                response.ErrorMessages.Add("Rating must be between 0 and 10.");
                response.StatusCode = HttpStatusCode.BadRequest;
                return response;
            }

            // Update the rating
            reviewToEdit.RatingItem.Rating = ratingItemDto.Rating;
            // Optionally update the timestamp
            reviewToEdit.RatingItem.CreatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            // Map back to DTO
            var resultDto = _mapper.Map<RatingItemDto>(reviewToEdit.RatingItem);
            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = resultDto;
            return response;
        }
        public async Task<APIResponse<RatingItemDto>> RemoveRatingAsync(string userId, int reviewId, bool isModerator = false)
        {
            var response = new APIResponse<RatingItemDto>
            {
                IsSuccess = false,
                StatusCode = HttpStatusCode.BadRequest
            };
            var review = await _dbContext.ReviewItems
                .Include(r => r.User)
                .Include(r => r.Book)
                .Include(r => r.RatingItem)
                .FirstOrDefaultAsync(r => r.Id == reviewId);

            if (review == null || review.RatingItem == null)
            {
                response.ErrorMessages.Add("Rating not found.");
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }
            if (review.UserId != userId && !isModerator)
            {
                SetNoReviewPermission(response);
                return response;
            }

            // Remove the RatingItem from the DbContext
            _dbContext.RatingItems.Remove(review.RatingItem);

            // Set the navigation property to null
            review.RatingItem = null;

            await _dbContext.SaveChangesAsync();

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = null; // Rating is deleted, so return null
            return response;
        }

        private static void SetNoReviewPermission<T>(APIResponse<T> response)
        {
            response.ErrorMessages.Add("You do not have permission to change this review.");
            response.StatusCode = HttpStatusCode.Forbidden;
        }
    }
}
