using NyriaNest.Domain.Enums;

namespace NyriaNest.Application.DTOs.Friends
{
    /// <summary>
    /// A friendship or request seen from one user's side (#84): "friend" is the other person,
    /// and "I share" / "friend shares" are already turned the right way round.
    /// </summary>
    public class FriendshipDto
    {
        public int Id { get; set; }
        public string FriendUserId { get; set; } = "";
        public string FriendUserName { get; set; } = "";
        public FriendshipStatus Status { get; set; }

        /// <summary>True when I sent the request, false when the friend did.</summary>
        public bool SentByMe { get; set; }

        public DateTime RequestedAt { get; set; }

        /// <summary>"Friends since"; null until accepted.</summary>
        public DateTime? AcceptedAt { get; set; }

        public bool ISharePhysicalLibrary { get; set; }
        public bool IShareEbooks { get; set; }
        public bool FriendSharesPhysicalLibrary { get; set; }
        public bool FriendSharesEbooks { get; set; }
    }

    /// <summary>Everything the Friends page shows (#84), including "7 of 10" for each limit.</summary>
    public class FriendsOverviewDto
    {
        public List<FriendshipDto> Friends { get; set; } = [];

        /// <summary>Requests I sent that are waiting for an answer.</summary>
        public List<FriendshipDto> Sent { get; set; } = [];

        /// <summary>Requests sent to me that I haven't answered.</summary>
        public List<FriendshipDto> Received { get; set; } = [];

        public int PhysicalLibraryUsed { get; set; }
        public int PhysicalLibraryLimit { get; set; }
        public int EbooksUsed { get; set; }
        public int EbooksLimit { get; set; }
    }

    /// <summary>Someone found by user name, to send a request to. Only the user name, nothing personal.</summary>
    public class UserSearchResultDto
    {
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
    }
}
