using NyriaNest.Domain.Entities;
using System.Linq.Expressions;

namespace NyriaNest.Api.Repositories
{
    public interface IUserRepo
    {
        Task CreateAsync<T>(T entity) where T : User;
        Task DeleteAsync<T>(T entity) where T : User;
        Task UpdateAsync<T>(T entity) where T : User;
        Task<IEnumerable<T>> GetAllAsync<T>() where T : User;
        Task<T> GetByIdAsync<T>(string id) where T : User;
        Task<T> GetByEmailAsync<T>(string email) where T : User;
        Task<T> GetByUserNameAsync<T>(string userName) where T : User;

        Task SaveAsync();
        IQueryable<T> FindByCondition<T>(Expression<Func<T, bool>> expression) where T : User;
    }
}
