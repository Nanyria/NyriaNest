using AutoMapper;
using NyriaNest.Application.DTOs;
using NyriaNest.Application.DTOs.Books;
using NyriaNest.Application.DTOs.History;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Domain.Entities;

namespace NyriaNest.Application.Mapping
{
    public class MappingConfig : Profile
    {
        public MappingConfig()
        {
            CreateMap<User, UserDto>()
                .ForMember(dest => dest.UserId, opt => opt.MapFrom(src => src.Id)); // Only needed for Id/UserId

            CreateMap<UserDto, User>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.UserId))
                .ForMember(dest => dest.CheckedOutBooks, opt => opt.Ignore())
                .ForMember(dest => dest.ReservedBooks, opt => opt.Ignore())
                .ForMember(dest => dest.UserHistory, opt => opt.Ignore())
                .ForMember(dest => dest.ReadList, opt => opt.Ignore())
                .ForMember(dest => dest.Reviews, opt => opt.Ignore())
                .ForMember(dest => dest.Notifications, opt => opt.Ignore());

            CreateMap<Book, SlimBookDto>();
            CreateMap<SlimBookDto, Book>()
                .ForMember(dest => dest.BookId, opt => opt.Ignore())
                .ForMember(dest => dest.StatusHistory, opt => opt.Ignore())
                .ForMember(dest => dest.Reservations, opt => opt.Ignore())
                .ForMember(dest => dest.CheckedOutBy, opt => opt.Ignore())
                .ForMember(dest => dest.Reviews, opt => opt.Ignore());

            CreateMap<User, CreateUserDto>();
            CreateMap<CreateUserDto, User>();


            CreateMap<User, UpdateUserAsAdminDto>();
            CreateMap<UpdateUserAsAdminDto, User>()
                            .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

            CreateMap<User, UpdateUserDto>();
            CreateMap<UpdateUserDto, User>()
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

            CreateMap<CreateAdminUserDto, User>().ReverseMap();

            CreateMap<StatusHistoryItem, StatusHistoryItemDto>().ReverseMap();
            CreateMap<ReservationItem, ReservationItemDto>()
                .ForMember(dest => dest.Book, opt => opt.MapFrom(src => src.Book))
                .ReverseMap()
                .ForMember(dest => dest.User, opt => opt.Ignore());
            CreateMap<CheckedOutItem, CheckedOutItemDto>()
                .ForMember(dest => dest.Book, opt => opt.MapFrom(src => src.Book))
                .ReverseMap()
                .ForMember(dest => dest.User, opt => opt.Ignore());

            CreateMap<NotificationItem, NotificationItemDto>().ReverseMap();
            CreateMap<ReviewItem, ReviewItemDto>()
                .ForMember(dest => dest.Book, opt => opt.MapFrom(src => src.Book))
                .ForMember(dest => dest.RatingItem, opt => opt.MapFrom(src => src.RatingItem))
                .ReverseMap()
                .ForMember(dest => dest.User, opt => opt.Ignore())
                .ForMember(dest => dest.Book, opt => opt.Ignore())
                .ForMember(dest => dest.RatingItem, opt => opt.Ignore());

            // ReviewItem <-> CreateReviewItemDto (if you use this for creation)
            CreateMap<ReviewItem, CreateReviewItemDto>()
                .ForMember(dest => dest.RatingItem, opt => opt.MapFrom(src => src.RatingItem))
                .ReverseMap()
                .ForMember(dest => dest.User, opt => opt.Ignore())
                .ForMember(dest => dest.Book, opt => opt.Ignore())
                .ForMember(dest => dest.RatingItem, opt => opt.Ignore());
            CreateMap<ReviewItem, BookReviewDto>()
                .ForMember(dest => dest.RatingItem, opt => opt.MapFrom(src => src.RatingItem))
                .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User.UserName)) // <-- This line
                .ReverseMap()
                .ForMember(dest => dest.User, opt => opt.Ignore())
                .ForMember(dest => dest.Book, opt => opt.Ignore())
                .ForMember(dest => dest.RatingItem, opt => opt.Ignore());
            // RatingItem <-> RatingItemDto
            CreateMap<RatingItem, RatingItemDto>().ReverseMap();
            CreateMap<CreateRatingItemDto, RatingItem>().ReverseMap();
            // FavoriteItem <-> FavoriteItemDto
            CreateMap<FavoriteItem, FavoriteItemDto>().ReverseMap();

        }
    }
}
