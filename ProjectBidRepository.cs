using BetterBusiness.Common.Entity;
using BetterBusiness.Core.DbContext;
using BetterBusiness.Core.Interface.Repository;
using Microsoft.EntityFrameworkCore;
using Recipe.NetCore.Base.Generic;
using Recipe.NetCore.Base.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BetterBusiness.Repository
{
    /// <summary>
    /// Repository class for Project Bidding.
    /// </summary>
    public class ProjectBidRepository : AuditableRepository<ProjectBid, Guid, BetterBusineesDbContext>, IProjectBidRepository
    {
        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="requestInfo"></param>
        public ProjectBidRepository(IRequestInfo<BetterBusineesDbContext> requestInfo) : base(requestInfo)
        {
        }

        #endregion

        #region Repository Functions

        /// <summary>
        /// Repo Function to get count of providers projects by statusid.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="statusId"></param>
        /// <returns></returns>
        public async Task<int> ProjectCountByProviderAndStatus(Guid id, Guid statusId)
        {
            return await Query(x => !x.IsDeleted && x.ProviderId == id && x.Project.ProjectStatusId == statusId).GetCountAsync();
        }

        /// <summary>
        /// Repository function to get bid information by bid id.
        /// </summary>
        /// <param name="bidId"></param>
        /// <returns></returns>
        public async Task<ProjectBid> GetBidById(Guid bidId)
        {
            return (await Query(x => !x.IsDeleted && x.Id == bidId && !x.IsCanceled)
                .IncludeInCore(x => x.Include(x => x.Project).ThenInclude(y => y.Seeker)).SelectAsync()).SingleOrDefault();
        }

        /// <summary>
        /// Repository function to get by projectid and providerid.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="providerId"></param>
        /// <returns></returns>
        public async Task<ProjectBid> ChecIfExistsProjectAndProviderId(Guid projectId, Guid providerId)
        {
            return (await Query(x => !x.IsDeleted && !x.IsCanceled && x.ProviderId == providerId && x.ProjectId == projectId).SelectAsync()).FirstOrDefault();
        }

        /// <summary>
        /// Repository funcion to get list of rejected bidders.
        /// </summary>
        /// <param name="bidId"></param>
        /// <param name="providerId"></param>
        /// <returns></returns>
        public async Task<List<ProjectBid>> GetListOfRejectedBidders(Guid bidId, Guid providerId)
        {
            return (await Query(x => !x.IsDeleted && !x.IsCanceled && x.ProjectId == bidId && x.ProviderId != providerId)
                .IncludeInCore(x => x.Include(y => y.Provider).ThenInclude(z => z.User))
                .SelectAsync()).ToList();
        }

        #endregion

    }
}

