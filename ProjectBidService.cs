using AutoMapper;
using BetterBusiness.Common;
using BetterBusiness.Common.Dto;
using BetterBusiness.Common.Entity;
using BetterBusiness.Common.Enums;
using BetterBusiness.Common.RequestModels;
using BetterBusiness.Core.Interface.Repository;
using BetterBusiness.Core.Interface.Service;
using Recipe.NetCore.Base.Abstract;
using Recipe.NetCore.Base.Generic;
using Recipe.NetCore.Base.Interface;
using System;
using System.Threading.Tasks;
using static BetterBusiness.Common.ConstantsCommon;

namespace BetterBusiness.Service
{
    /// <summary>
    /// Service class for Project Bidding
    /// </summary>
    public class ProjectBidService : Service<IProjectBidRepository, ProjectBid, ProjectBidModel, Guid>, IProjectBidService
    {
        #region Properties

        private readonly IMapper _mapper;
        private readonly IUserService _userService;
        private readonly ISeekerService _seekerService;
        private readonly IProjectService _projectService;
        private readonly IProviderService _providerService;
        private readonly IUserLoggedinService _userLoggedinService;
        private readonly IAppConfigurationService _appConfigurationService;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="unitOfWork"></param>
        /// <param name="repository"></param>
        /// <param name="mapper"></param>
        /// <param name="userService"></param>
        /// <param name="projectService"></param>
        /// <param name="providerService"></param>
        /// <param name="seekerService"></param>
        /// <param name="userLoggedinService"></param>
        public ProjectBidService(IUnitOfWork unitOfWork, IProjectBidRepository repository, IMapper mapper,
            IUserService userService, IProjectService projectService, IProviderService providerService,
            ISeekerService seekerService, IUserLoggedinService userLoggedinService, IAppConfigurationService appConfigurationService)
            : base(unitOfWork, repository, mapper)
        {
            _mapper = mapper;
            _userService = userService;
            _projectService = projectService;
            _seekerService = seekerService;
            _providerService = providerService;
            _userLoggedinService = userLoggedinService;
            _appConfigurationService = appConfigurationService;
        }

        #endregion

        #region Service Functions

        #region Fetch Calls

        /// <summary>
        /// Service function to get bid information.
        /// </summary>
        /// <param name="bidId"></param>
        /// <returns></returns>
        public async Task<DataTransferObject<ProjectBidViewModel>> GetBidById(Guid bidId)
        {
            //1. Fetch bid information from repository,
            var projectBid = await Repository.GetBidById(bidId);

            //2. Check if bid is null.
            if (projectBid == null)
            {
                new DataTransferObject<ProjectBidViewModel>();
            }

            //3. Mapped entity to model.
            var mappedModel = _mapper.Map<ProjectBidViewModel>(projectBid);

            double paymentProcessingFee = double.Parse(await _appConfigurationService.GetValueByName("PaymentProcessingFee"));

            //4. Filling amount to be paid.
            var processingFee = (mappedModel.BidAmount / 100) * paymentProcessingFee;
            mappedModel.AmountToBePaid = Utility.TurncateToPrecision(mappedModel.BidAmount + processingFee, 2);
            mappedModel.BidAmount = Utility.TurncateToPrecision(mappedModel.BidAmount, 2);

            //5. Return.
            return new DataTransferObject<ProjectBidViewModel>() { Result = mappedModel };
        }

        #endregion

        #region Project Bid's Functions

        /// <summary>
        /// Service function to bid on project.
        /// </summary>
        /// <param name="projectBidModel"></param>
        /// <returns></returns>
        public async Task<bool> BidOnProject(ProjectBidRequestModel projectBidModel)
        {
            //1. Check user if he is approved.
            var userModel = _userLoggedinService.GetLoggedinUser();
            var user = await _userService.GetAsync(userModel.Id);
            if (user.Result.UserStatusId != UserStatuses.Approved)
            {
                return false;
            }

            //2. Check if bid exists already.
            var projectBidExistsAlready = await Repository.ChecIfExistsProjectAndProviderId(projectBidModel.ProjectId, projectBidModel.ProviderId);
            if (projectBidExistsAlready != null)
            {
                return false;
            }

            //3. Check if project exists or doesn't.
            var projectModel = await _projectService.GetProjectEntityById(projectBidModel.ProjectId);
            if (projectModel == null)
            {
                return false;
            }

            //4. Check if project is on open state.
            if (projectModel.ProjectStatusId != Open)
            {
                return false;
            }

            //5. Check if bid amount is within range.
            if (projectModel.BudgetFrom != null || projectModel.BudgetTo != null)
            {
                if (!(projectBidModel.BidAmount >= projectModel.BudgetFrom && projectBidModel.BidAmount <= projectModel.BudgetTo))
                {
                    return false;
                }
            }

            //6. Check if Provider exists.
            var providerModel = await _providerService.GetProviderEntityById(projectBidModel.ProviderId);
            if (providerModel == null)
            {
                return false;
            }

            //7. Setting up project bid.
            var mappedProjectBid = _mapper.Map<ProjectBidModel>(projectBidModel);

            var percentageForNew = double.Parse(await _appConfigurationService.GetValueByName("PercentageForNew"));

            mappedProjectBid.Score = Utility.GenerateEligibilityScore(projectModel, providerModel, percentageForNew);
            mappedProjectBid.GeoScore = Utility.FindGeoScore(projectModel, providerModel);

            //8. Creating project bid,
            var createdProjectBid = await CreateAsync(mappedProjectBid);

            //9. Returning response.
            if (createdProjectBid.Result != null)
            {
                var seekerModel = await _seekerService.GetSeekerById(projectModel.SeekerId);
                var seekerEmailModel = new SeekerEmailModel()
                {
                    IsIndividual = seekerModel.Result.IsIndividual,
                    ProjectName = projectModel.Title,
                    CompanyName = seekerModel.Result.CompanyName,
                    FirstName = seekerModel.Result.FirstName,
                    LastName = seekerModel.Result.LastName,
                    Email = seekerModel.Result.Email
                };

                //6. Fetch seeker name from seeker model.
                var seekerName = seekerModel.Result.IsIndividual ? $" {seekerModel.Result.FirstName} {seekerModel.Result.FirstName}" : seekerModel.Result.CompanyName;

                //7. Send email to provider.
                await EmailService.OnProjectBid(seekerEmailModel, seekerModel.Result.Language, seekerName, projectModel.Id.ToString());
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Service function for approving bid.
        /// </summary>
        /// <param name="bidId"></param>
        /// <returns></returns>
        public async Task<bool> ApproveBid(Guid bidId, string paymentMethod = null)
        {
            //1. Find project bid and check if doesn't exists return false.
            var bid = await GetAsync(bidId);
            if (bid == null || bid.Result == null)
            {
                return false;
            }

            //2. Check if bidder's request is not canceled.
            if (bid.Result.IsCanceled)
            {
                return false;
            }

            //3. Check if project exists or doesn't.
            var projectModel = await _projectService.GetAsync(bid.Result.ProjectId);
            if (projectModel == null || projectModel.Result == null)
            {
                return false;
            }

            //4. Check if project is on open state.
            if (projectModel.Result.ProjectStatusId != Open)
            {
                return false;
            }

            //5. Check if bid amount is within range.
            if (projectModel.Result.BudgetFrom != null || projectModel.Result.BudgetTo != null)
            {
                if (!(bid.Result.BidAmount >= projectModel.Result.BudgetFrom && bid.Result.BidAmount <= projectModel.Result.BudgetTo))
                {
                    return false;
                }
            }

            //6. Check if Provider exists.
            var providerModel = await _providerService.GetProviderById(bid.Result.ProviderId);
            if (providerModel == null || providerModel.Result == null)
            {
                return false;
            }

            //7. Updating values for bid approval.
            projectModel.Result.ProjectBidId = bid.Result.Id;
            if (paymentMethod == PaymentMethod.Other) 
            { 
                projectModel.Result.ProjectStatusId = PendingPayment; 
            }
            else
            {
                projectModel.Result.ProjectStatusId = InProgress;
                projectModel.Result.StartDate = DateTime.Now;
                if (projectModel.Result.EndType == (int)ProjectEndDateEnum.WorkingDays)
                {
                    projectModel.Result.EndDate = projectModel.Result.StartDate.Value.AddDays(projectModel.Result.EndDays.Value);
                }
            }

            //8. Updaing project.
            var updatedProject = await _projectService.UpdateAsync(projectModel.Result);

            //9. Checking if project is updated successfully.
            if (updatedProject == null || updatedProject.Result == null)
            {
                return false;
            }

            //10.a.  if payment method is other, then do not send approval email to bidder from here i.e. wait for payment approval by admin
            if (paymentMethod == PaymentMethod.Other)
            {
                return true;
            }

            //10. Setup provider.
            var providerEmail = new ProviderEmailModel()
            {
                IsIndividual = providerModel.Result.IsIndividual,
                ProjectName = projectModel.Result.Title,
                CompanyName = providerModel.Result.CompanyName,
                FirstName = providerModel.Result.FirstName,
                LastName = providerModel.Result.LastName,
                Email = providerModel.Result.Email
            };

            
            //11. Fetch seeker name from seeker model.
            var seekerModel = await _seekerService.GetSeekerById(projectModel.Result.SeekerId);
            var seekerName = seekerModel.Result.IsIndividual ? $" {seekerModel.Result.FirstName} {seekerModel.Result.FirstName}" : seekerModel.Result.CompanyName;

            //12.1 Sending email to bidder whose bid is approved.
            await EmailService.SendBidApprovalEmail(providerEmail, providerModel.Result.Language, seekerName);

            //12.2 Finding other bidder's whose bids are rejected.

            var listOfRejectedBidders = await Repository.GetListOfRejectedBidders(projectModel.Result.Id, bid.Result.ProviderId);

            //12.3 Sending email to rejected bidders.
            foreach (var item in listOfRejectedBidders)
            {
                var providerName = item.Provider.IsIndividual ? $" {item.Provider.FirstName} {item.Provider.FirstName}" : item.Provider.CompanyName;

                await EmailService.SendBidRejectedEmails(providerName, providerEmail.ProjectName, item.Provider.User.Email, item.Provider.User.Language, seekerName);
            }

            //13. Returning resoponse.
            return true;
        }

        /// <summary>
        /// Service function for canceling bid.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="providerId"></param>
        /// <returns></returns>
        public async Task<bool> CancelBid(Guid projectId, Guid providerId)
        {
            //1. Check if current logged in user same who is requesting.
            var loggedInUser = _userLoggedinService.GetLoggedinUser();

            if (loggedInUser.Role.Equals(Roles.Provider) && loggedInUser.ProviderId != providerId)
            {
                return false;
            }

            //2. Check if bid exists already.
            var projectBidExistsAlready = await Repository.ChecIfExistsProjectAndProviderId(projectId, providerId);
            if (projectBidExistsAlready == null)
            {
                return false;
            }

            //Check if already cancelled.
            if (projectBidExistsAlready.IsCanceled)
            {
                return false;
            }

            //2. Check if project exists or doesn't.
            var projectModel = await _projectService.GetAsync(projectId);
            if (projectModel == null || projectModel.Result == null)
            {
                return false;
            }

            //3. Check if project is on open state.
            if (projectModel.Result.ProjectStatusId != Open)
            {
                return false;
            }

            //4. Updating bid entry in database for cancel bid.
            var mappedBidModel = _mapper.Map<ProjectBidModel>(projectBidExistsAlready);
            mappedBidModel.IsCanceled = true;
            var updatedBidModel = await UpdateAsync(mappedBidModel);

            //5. Check if bid rejected by seeker we do send email to provider
            if (loggedInUser.Role.Equals(Roles.Seeker))
            {
                var providerModel = await _providerService.GetProviderById(providerId);
                var seekerModel = await _seekerService.GetAsync(projectModel.Result.SeekerId);
                var providerEmail = new ProviderEmailModel()
                {
                    IsIndividual = providerModel.Result.IsIndividual,
                    ProjectName = projectModel.Result.Title,
                    CompanyName = providerModel.Result.CompanyName,
                    FirstName = providerModel.Result.FirstName,
                    LastName = providerModel.Result.LastName,
                    Email = providerModel.Result.Email
                };

                //6. Fetch seeker name from seeker model.
                var seekerName = seekerModel.Result.IsIndividual ? $" {seekerModel.Result.FirstName} {seekerModel.Result.FirstName}" : seekerModel.Result.CompanyName;

                //7. Send email to provider.
                await EmailService.SendBidRejectedEmail(providerEmail, providerModel.Result.Language, seekerName);
            }

            //8. Returning response.
            if (updatedBidModel == null || updatedBidModel.Result == null)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        #endregion

        #endregion
    }
}
