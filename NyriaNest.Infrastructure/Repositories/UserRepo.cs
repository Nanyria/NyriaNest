using Microsoft.EntityFrameworkCore;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using NyriaNest.Infrastructure.Data;
using System.Linq.Expressions;

namespace NyriaNest.Infrastructure.Repositories
{
    public class UserRepo : IUserRepo
    {
        private readonly AppDbContext _db;

        public UserRepo(AppDbContext db)
        {
            _db = db;
        }

        public async Task CreateAsync<T>(T entity) where T : User
        {
            await _db.Set<T>().AddAsync(entity);
        }

        public async Task DeleteAsync<T>(T entity) where T : User
        {
            _db.Set<T>().Remove(entity);
        }

        public async Task UpdateAsync<T>(T entity) where T : User
        {
            _db.Set<T>().Update(entity);
        }

        public async Task<IEnumerable<T>> GetAllAsync<T>() where T : User
        {
            return await _db.Set<T>()
                .Include(u => u.ReservedBooks)
                .Include(u => u.CheckedOutBooks)
                .ToListAsync();
        }

        public async Task<T> GetByIdAsync<T>(string id) where T : User
        {
            return await _db.Set<T>()
                .Include(u => u.ReservedBooks)
                    .ThenInclude(r => r.Book)
                .Include(u => u.CheckedOutBooks)
                     .ThenInclude(r => r.Book)
                .Include(u => u.ReadList)
                    .ThenInclude(f => f.Book)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<T> GetByEmailAsync<T>(string email) where T : User
        {
            return await _db.Set<T>()
                .Include(u => u.ReservedBooks)
                .Include(u => u.CheckedOutBooks)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<T> GetByUserNameAsync<T>(string userName) where T : User
        {
            return await _db.Set<T>()
                .Include(u => u.UserHistory)
                .Include(u => u.ReservedBooks)
                .Include(u => u.CheckedOutBooks)
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == userName.ToLower());
        }

        public async Task SaveAsync()
        {
            await _db.SaveChangesAsync();
        }

        public IQueryable<T> FindByCondition<T>(Expression<Func<T, bool>> expression) where T : User
        {
            return _db.Set<T>().Where(expression);
        }
    }
}
