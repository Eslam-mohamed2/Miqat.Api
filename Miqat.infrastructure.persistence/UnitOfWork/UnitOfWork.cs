using Microsoft.EntityFrameworkCore;
using Miqat.Application.Interfaces;
using Miqat.infrastructure.persistence.Data;
using Miqat.infrastructure.persistence.Repositories.GenericRepository;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.UnitOfWork
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly MiqatDbContext _Context;
        private Hashtable _repositories;
        public UnitOfWork(MiqatDbContext Context) => _Context = Context;
        public async Task<int> CompleteAsync()
        {
            return await _Context.SaveChangesAsync();
        }
        public void Dispose() => _Context.Dispose();
        public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class
        {
            if (_repositories == null)
                _repositories = new Hashtable();
            var type = typeof(TEntity).Name;
            if (!_repositories.ContainsKey(type))
            {
                var repositoryType = typeof(GenericRepository<>);
                var repositoryInstance = Activator.CreateInstance(repositoryType.MakeGenericType(typeof(TEntity)), _Context);
                _repositories.Add(type, repositoryInstance);
            }
            return (IGenericRepository<TEntity>)_repositories[type]!;
        }
    }
}
