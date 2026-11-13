namespace NyriaNest.Application.DTOs
{
    // One page of results plus the total, so the UI can show "1–10 of 42"
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
