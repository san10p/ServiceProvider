using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Recipe.NetCore.Base.Generic;
using Recipe.NetCore.Base.Interface;

namespace Recipe.NetCore.Base.Generic
{
    public sealed class QueryFilter<TEntity, TKey, TDbContext> : IQueryFilter<TEntity>
        where TEntity : class, IBase<TKey>
        where TKey : IEquatable<TKey>
        where TDbContext : DbContext
    {
        #region Private Fields

        private readonly Expression<Func<TEntity, bool>> _expression;
        private readonly List<Expression<Func<TEntity, object>>> _includes;
        private readonly Repository<TEntity, TKey, TDbContext> _repository;
        private Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> _orderBy;
        private Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>> _includeInCore;
        #endregion Private Fields

        #region Constructors

        public QueryFilter(Repository<TEntity, TKey, TDbContext> repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            _repository = repository;
            _includes = new List<Expression<Func<TEntity, object>>>();
        }

        public QueryFilter(Repository<TEntity, TKey, TDbContext> repository, Expression<Func<TEntity, bool>> expression)
            : this(repository)
        {
            _expression = expression;
        }

        #endregion Constructors

        public IQueryFilter<TEntity> OrderBy(Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderByExpression)
        {
            _orderBy = orderByExpression;
            return this;
        }

        //This is Ef way of including entities 
        public IQueryFilter<TEntity> Include(Expression<Func<TEntity, object>> expression)
        {
            _includes.Add(expression);
            return this;
        }

        //This is Ef Core way of including entities 
        public IQueryFilter<TEntity> IncludeInCore(Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>> expression)
        {
            this._includeInCore = expression;
            return this;
        }
        public async Task<Tuple<int, IEnumerable<TEntity>>> SelectPageAsync(Expression<Func<TEntity, bool>> filter = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null,
            List<Expression<Func<TEntity, object>>> includes = null, int? page = null, int? pageSize = null)
        {
            return await _repository.GetPagedResultAsync(filter, orderBy, includes, page, pageSize);
        }

        public IEnumerable<TEntity> Select()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector = null)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<TEntity>> SelectAsync()
        {
            return await _repository.SelectAsync(_expression, _orderBy, _includes, _includeInCore);
        }

        public async Task<int> GetCountAsync()
        {
            return await _repository.GetCountAsync(_expression, _orderBy, _includes, _includeInCore);
        }
    }
}
