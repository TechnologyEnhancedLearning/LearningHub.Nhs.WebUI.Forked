﻿namespace LearningHub.Nhs.Services
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Threading.Tasks;
    using AutoMapper;
    using LearningHub.Nhs.Api.Shared.Configuration;
    using LearningHub.Nhs.Models.Entities.Activity;
    using LearningHub.Nhs.Models.Entities.Resource;
    using LearningHub.Nhs.Models.Enums;
    using LearningHub.Nhs.Models.MyLearning;
    using LearningHub.Nhs.Models.Report.ReportCreate;
    using LearningHub.Nhs.Models.Resource.Blocks;
    using LearningHub.Nhs.Repository.Helpers;
    using LearningHub.Nhs.Repository.Interface.Activity;
    using LearningHub.Nhs.Repository.Interface.Hierarchy;
    using LearningHub.Nhs.Services.Interface;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json.Linq;
    using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

    /// <summary>
    /// The rating service.
    /// </summary>
    public class MyLearningService : IMyLearningService
    {
        /// <summary>
        /// The resourceActivityRepository.
        /// </summary>
        private readonly IResourceActivityRepository resourceActivityRepository;
        private readonly ICatalogueNodeVersionRepository catalogueNodeVersionRepository;

        /// <summary>
        /// The mediaResourcePlayedSegmentRepository.
        /// </summary>
        private readonly IMediaResourcePlayedSegmentRepository mediaResourcePlayedSegmentRepository;

        /// <summary>
        /// The assessment resource activity repository.
        /// </summary>
        private readonly IAssessmentResourceActivityRepository assessmentResourceActivityRepository;

        /// <summary>
        /// The assessment resource activity interaction repository.
        /// </summary>
        private readonly IAssessmentResourceActivityInteractionRepository assessmentResourceActivityInteractionRepository;

        /// <summary>
        /// The scorm resource activity interaction repository.
        /// </summary>
        private readonly IScormActivityRepository scormActivityRepository;

        /// <summary>
        /// The media resource activity interaction repository.
        /// </summary>
        private readonly IMediaResourceActivityRepository mediaResourceActivity;

        /// <summary>
        /// The mapper.
        /// </summary>
        private readonly IMapper mapper;

        /// <summary>
        /// The settings.
        /// </summary>
        private readonly IOptions<Settings> settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="MyLearningService"/> class.
        /// </summary>
        /// <param name="resourceActivityRepository">The resource activity repository.</param>
        /// <param name="catalogueNodeVersionRepository">The catalogue node repository.</param>
        /// <param name="mediaResourcePlayedSegmentRepository">The mediaResourcePlayedSegmentRepository.</param>
        /// <param name="assessmentResourceActivityRepository">The assessmentResourceActivityRepository.</param>
        /// <param name="assessmentResourceActivityInteractionRepository">The assessmentResourceActivityInteractionRepository.</param>
        /// <param name="mapper">The mapper.</param>
        /// <param name="settings">The settings.</param>
        /// <param name="scormActivityRepository">The scormActivityRepository.</param>
        /// <param name="mediaResourceActivity">The mediaResourceActivity.</param>
        public MyLearningService(
            IResourceActivityRepository resourceActivityRepository,
            IMediaResourcePlayedSegmentRepository mediaResourcePlayedSegmentRepository,
            IAssessmentResourceActivityRepository assessmentResourceActivityRepository,
            IAssessmentResourceActivityInteractionRepository assessmentResourceActivityInteractionRepository,
            ICatalogueNodeVersionRepository catalogueNodeVersionRepository,
            IMapper mapper,
            IOptions<Settings> settings,
            IScormActivityRepository scormActivityRepository,
            IMediaResourceActivityRepository mediaResourceActivity)
        {
            this.resourceActivityRepository = resourceActivityRepository;
            this.mediaResourcePlayedSegmentRepository = mediaResourcePlayedSegmentRepository;
            this.assessmentResourceActivityRepository = assessmentResourceActivityRepository;
            this.catalogueNodeVersionRepository = catalogueNodeVersionRepository;
            this.assessmentResourceActivityInteractionRepository = assessmentResourceActivityInteractionRepository;
            this.mapper = mapper;
            this.settings = settings;
            this.scormActivityRepository = scormActivityRepository;
            this.mediaResourceActivity = mediaResourceActivity;
        }

        /// <summary>
        /// Gets the activity records for the detailed activity tab of My Learning screen.
        /// </summary>
        /// /// <param name="userId">The user id.</param>
        /// <param name="requestModel">The request model.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        public async Task<MyLearningDetailedViewModel> GetActivityDetailed(int userId, MyLearningRequestModel requestModel)
        {
            var activityQuery = this.resourceActivityRepository.GetByUserIdFromSP(userId, requestModel, this.settings.Value.DetailedMediaActivityRecordingStartDate).Result.OrderByDescending(r => r.ActivityStart).DistinctBy(l => l.Id);

            // Count total records.
            MyLearningDetailedViewModel viewModel = new MyLearningDetailedViewModel()
            {
                TotalCount = this.resourceActivityRepository.GetTotalCount(userId, requestModel, this.settings.Value.DetailedMediaActivityRecordingStartDate),
            };

            // Return only the requested batch.
            var activityEntities = activityQuery.ToList();

            viewModel.Activities = await this.PopulateMyLearningDetailedItemViewModels(activityEntities, userId);

            return viewModel;
        }

        /// <summary>
        /// Gets the played segment data for the progress modal in My Learning screen.
        /// </summary>
        /// /// <param name="userId">The user id.</param>
        /// <param name="resourceId">The resourceId.</param>
        /// <param name="majorVersion">The majorVersion.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        public async Task<List<PlayedSegmentViewModel>> GetPlayedSegments(int userId, int resourceId, int majorVersion)
        {
            var playedSegments = await this.mediaResourcePlayedSegmentRepository.GetPlayedSegmentsAsync(userId, resourceId, majorVersion);
            var viewModel = this.mapper.Map<List<PlayedSegmentViewModel>>(playedSegments);

            return viewModel;
        }

        /// <summary>
        /// Gets the resource certificate details of a resource reference.
        /// </summary>
        /// /// <param name="userId">The user id.</param>
        /// <param name="resourceReferenceId">The resourceReferenceId.</param>
        /// <param name="majorVersion">The majorVersion.</param>
        /// <param name="minorVersion">The minorVersion.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        public async Task<Tuple<int, MyLearningDetailedItemViewModel>> GetResourceCertificateDetails(int userId, int resourceReferenceId, int majorVersion, int minorVersion)
        {
            MyLearningDetailedItemViewModel myLearningDetailedItemViewModel = null;
            var activityQuery = this.resourceActivityRepository.GetByUserId(userId);
            if (majorVersion > 0)
            {
                activityQuery = activityQuery.Where(x => x.MajorVersion == majorVersion && x.MinorVersion == minorVersion);
            }
            else
            {
                // filter and use the latest version only
                activityQuery = activityQuery.Where(rv => rv.ResourceVersionId == rv.Resource.CurrentResourceVersionId);
            }

            activityQuery = activityQuery.Where(x => x.Resource.ResourceReference.FirstOrDefault() != null && x.Resource.ResourceReference.Any(rr => rr.OriginalResourceReferenceId == resourceReferenceId));
            int totalNumberOfAccess = activityQuery.Count();
            var activityEntities = await activityQuery.OrderByDescending(x => x.Score).ThenByDescending(x => x.ActivityStart).ToListAsync();
            activityEntities.RemoveAll(x => x.Resource.ResourceTypeEnum == ResourceTypeEnum.Scorm && (x.ActivityStatusId == (int)ActivityStatusEnum.Downloaded || x.ActivityStatusId == (int)ActivityStatusEnum.Incomplete || x.ActivityStatusId == (int)ActivityStatusEnum.InProgress));
            if (activityEntities.Any() && activityEntities.FirstOrDefault()?.Resource.ResourceTypeEnum == ResourceTypeEnum.Assessment)
            {
                totalNumberOfAccess = activityQuery.SelectMany(x => x.AssessmentResourceActivity).OrderByDescending(a => a.CreateDate).ToList().Count();
                activityEntities = activityEntities.Where(x => x.AssessmentResourceActivity.FirstOrDefault() != null && x.AssessmentResourceActivity.FirstOrDefault().Score.HasValue && ((int)Math.Round(x.AssessmentResourceActivity.FirstOrDefault().Score.Value, MidpointRounding.AwayFromZero) >= x.ResourceVersion.AssessmentResourceVersion.PassMark)).ToList();
            }
            else if (activityEntities.Any() && (activityEntities.FirstOrDefault()?.Resource.ResourceTypeEnum == ResourceTypeEnum.Video || activityEntities.FirstOrDefault()?.Resource.ResourceTypeEnum == ResourceTypeEnum.Audio))
            {
                totalNumberOfAccess = activityQuery.SelectMany(x => x.MediaResourceActivity).OrderByDescending(a => a.CreateDate).ToList().Count();
            }

            if (activityEntities.Any())
            {
                ResourceActivity filteredActivity = null;
                if (activityEntities.FirstOrDefault()?.Resource.ResourceTypeEnum == ResourceTypeEnum.Audio || activityEntities.FirstOrDefault()?.Resource.ResourceTypeEnum == ResourceTypeEnum.Video)
                {
                    filteredActivity = activityEntities.Where(x => x.MediaResourceActivity != null && x.MediaResourceActivity.Any(x => x.PercentComplete == 100)).OrderByDescending(x => x.ActivityStart).FirstOrDefault();
                }
                else
                {
                    filteredActivity = activityEntities.Where(x => x.ActivityStatusId == ((int)ActivityStatusEnum.Completed) || x.ActivityStatusId == ((int)ActivityStatusEnum.Incomplete) || x.ActivityStatusId == ((int)ActivityStatusEnum.Passed) || x.ActivityStatusId == ((int)ActivityStatusEnum.Downloaded)).OrderByDescending(x => x.ActivityStart).FirstOrDefault();
                }

                if (filteredActivity != null)
                {
                    var result = await this.PopulateMyLearningDetailedItemViewModels(new List<ResourceActivity> { filteredActivity }, userId);
                    myLearningDetailedItemViewModel = result.FirstOrDefault();
                    if (filteredActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Scorm)
                    {
                        var sa = this.scormActivityRepository.GetAll().OrderByDescending(x => x.Id).Where(x => x.ResourceActivity.ResourceVersionId == filteredActivity.ResourceVersionId && x.CreateUserId == filteredActivity.UserId);
                        if (sa.Any())
                        {
                            myLearningDetailedItemViewModel.ActivityDurationSeconds = 0;
                            foreach (var item in sa.Where(x => x.CmiCoreLessonStatus.HasValue))
                            {
                                myLearningDetailedItemViewModel.ActivityDurationSeconds = myLearningDetailedItemViewModel.ActivityDurationSeconds + item.DurationSeconds;
                            }
                        }
                    }
                    else if (filteredActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Video || filteredActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Audio)
                    {
                        var ma = this.mediaResourceActivity.GetAll().OrderByDescending(x => x.Id).Where(x => x.ResourceActivity.ResourceVersionId == filteredActivity.ResourceVersionId && x.CreateUserId == filteredActivity.UserId);
                        if (ma.Any())
                        {
                            myLearningDetailedItemViewModel.ActivityDurationSeconds = 0;
                            foreach (var item in ma)
                            {
                                if (item.SecondsPlayed.HasValue)
                                {
                                    myLearningDetailedItemViewModel.ActivityDurationSeconds = myLearningDetailedItemViewModel.ActivityDurationSeconds + (int)item.SecondsPlayed;
                                }
                            }
                        }
                    }

                    return new Tuple<int, MyLearningDetailedItemViewModel>(totalNumberOfAccess, result.FirstOrDefault());
                }

                return new Tuple<int, MyLearningDetailedItemViewModel>(0, myLearningDetailedItemViewModel);
            }

            return new Tuple<int, MyLearningDetailedItemViewModel>(0, myLearningDetailedItemViewModel);
        }

        /// <inheritdoc/>
        public async Task<List<MyLearningDetailedItemViewModel>> PopulateMyLearningDetailedItemViewModels(List<ResourceActivity> resourceActivities, int userId)
        {
            var viewModels = new List<MyLearningDetailedItemViewModel>();

            var activityNodes = (from entity in resourceActivities
                                 let data = entity.NodePath.CatalogueNodeId
                                 select data).Distinct().ToArray();

            var activityCatalogues = this.catalogueNodeVersionRepository.GetAll()
                .Include(x => x.NodeVersion).Where(c => activityNodes.Contains(c.NodeVersion.NodeId));

            foreach (ResourceActivity resourceActivity in resourceActivities)
            {
                var viewModel = new MyLearningDetailedItemViewModel()
                {
                    Title = resourceActivity.ResourceVersion.Title,
                    ResourceId = resourceActivity.ResourceId,
                    MajorVersion = resourceActivity.MajorVersion,
                    MinorVersion = resourceActivity.MinorVersion,
                    Version = resourceActivity.MajorVersion + "." + resourceActivity.MinorVersion,
                    ResourceType = resourceActivity.Resource.ResourceTypeEnum,
                    ActivityDate = resourceActivity.ActivityStart ?? resourceActivity.CreateDate,
                    ActivityStatus = (ActivityStatusEnum)resourceActivity.ActivityStatusId,
                    IsCurrentResourceVersion = resourceActivity.ResourceVersionId == resourceActivity.Resource.CurrentResourceVersionId,
                    VersionStatusId = (int?)resourceActivity.ResourceVersion.VersionStatusEnum,
                };

                var latestActivityCheck = await this.resourceActivityRepository.GetAllTheActivitiesFromSP(resourceActivity.CreateUserId, resourceActivity.ResourceVersionId);
                var allAttempts = latestActivityCheck;
                latestActivityCheck.RemoveAll(x => x.Resource.ResourceTypeEnum == ResourceTypeEnum.Scorm && (x.ActivityStatusId == (int)ActivityStatusEnum.Downloaded || x.ActivityStatusId == (int)ActivityStatusEnum.Incomplete || x.ActivityStatusId == (int)ActivityStatusEnum.InProgress));
                if (latestActivityCheck.Any() && latestActivityCheck.FirstOrDefault()?.Resource.ResourceTypeEnum == ResourceTypeEnum.Assessment)
                {
                    latestActivityCheck = latestActivityCheck.Where(x => x.AssessmentResourceActivity.FirstOrDefault() != null && x.AssessmentResourceActivity.FirstOrDefault().Score.HasValue && ((int)Math.Round(x.AssessmentResourceActivity.FirstOrDefault().Score.Value, MidpointRounding.AwayFromZero) >= x.ResourceVersion.AssessmentResourceVersion.PassMark)).ToList();
                }

                ResourceActivity expectedActivity = null;
                if (resourceActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Audio || resourceActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Video)
                {
                    expectedActivity = latestActivityCheck.Where(x => x.LaunchResourceActivityId == resourceActivity.Id && resourceActivity.MediaResourceActivity != null && resourceActivity.MediaResourceActivity.Any(y => y.PercentComplete == 100)).OrderByDescending(y => y.ActivityStart).FirstOrDefault();
                }
                else
                {
                    expectedActivity = latestActivityCheck.Where(x => x.Id == resourceActivity.Id && (x.ActivityStatusId == ((int)ActivityStatusEnum.Completed) || x.ActivityStatusId == ((int)ActivityStatusEnum.Incomplete) || x.ActivityStatusId == ((int)ActivityStatusEnum.Passed) || x.ActivityStatusId == ((int)ActivityStatusEnum.Downloaded))).OrderByDescending(x => x.ActivityStart).FirstOrDefault();
                }

                if (latestActivityCheck.Any() && expectedActivity != null)
                {
                    bool isExpectedActivityIdMatched = false;
                    if (resourceActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Audio || resourceActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Video)
                    {
                        isExpectedActivityIdMatched = latestActivityCheck.OrderByDescending(x => x.ActivityStart).FirstOrDefault().Id == expectedActivity.LaunchResourceActivityId;
                    }
                    else
                    {
                        isExpectedActivityIdMatched = latestActivityCheck.OrderByDescending(x => x.ActivityStart).FirstOrDefault().Id == expectedActivity.Id;
                    }

                    viewModel.CertificateEnabled = isExpectedActivityIdMatched && resourceActivity.ResourceVersion.CertificateEnabled.GetValueOrDefault(false);
                }
                else
                {
                    viewModel.CertificateEnabled = false;
                }

                var catalogueNodeVersion = await activityCatalogues
                .SingleOrDefaultAsync(x => x.NodeVersion.NodeId == resourceActivity.NodePath.CatalogueNodeId);
                if (catalogueNodeVersion != null)
                {
                    viewModel.CertificateUrl = catalogueNodeVersion.CertificateUrl;
                }

                var resourceReference = resourceActivity.Resource.ResourceReference.FirstOrDefault(x => x.NodePathId == resourceActivity.NodePathId);

                if (resourceReference != null)
                {
                    viewModel.ResourceReferenceId = resourceReference.OriginalResourceReferenceId;
                }

                if (resourceActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Scorm)
                {
                    viewModel.ScorePercentage = resourceActivity.Score.HasValue ? (int)resourceActivity.Score : 0;
                    viewModel.MasteryScore = resourceActivity.ResourceVersion.ScormResourceVersion.ScormResourceVersionManifest.MasteryScore;
                    var sa = resourceActivity.ScormActivity.FirstOrDefault();
                    if (sa != null && sa.CmiCoreLessonStatus.HasValue)
                    {
                        viewModel.ActivityStatus = (ActivityStatusEnum)sa.CmiCoreLessonStatus;
                        viewModel.ActivityDurationSeconds = sa.DurationSeconds;
                        viewModel.ScorePercentage = sa.CmiCoreScoreRaw.HasValue ? (int)sa.CmiCoreScoreRaw : 0;
                    }
                }

                if (resourceActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Video || resourceActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Audio)
                {
                    if (resourceActivity.MediaResourceActivity != null && resourceActivity.MediaResourceActivity.Any())
                    {
                        viewModel.ActivityStatus = (resourceActivity.MediaResourceActivity.First().PercentComplete == 100) ? ActivityStatusEnum.Completed : ActivityStatusEnum.InProgress;
                        viewModel.CompletionPercentage = Convert.ToInt32(resourceActivity.MediaResourceActivity.First().PercentComplete.GetValueOrDefault());
                        viewModel.ActivityDurationSeconds = resourceActivity.MediaResourceActivity.First().SecondsPlayed.GetValueOrDefault();
                    }
                    else if (viewModel.ActivityDate >= this.settings.Value.DetailedMediaActivityRecordingStartDate)
                    {
                        // If media activity is from after the date when detailed activity recording began it should have an MRA. If it doesn't something has gone wrong.
                        viewModel.ActivityStatus = ActivityStatusEnum.InProgress;
                    }

                    if (resourceActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Video)
                    {
                        viewModel.ResourceDurationMilliseconds = resourceActivity.ResourceVersion.VideoResourceVersion.DurationInMilliseconds.GetValueOrDefault();
                    }
                    else if (resourceActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Audio)
                    {
                        viewModel.ResourceDurationMilliseconds = resourceActivity.ResourceVersion.AudioResourceVersion.DurationInMilliseconds.GetValueOrDefault();
                    }
                }

                if (resourceActivity.Resource.ResourceTypeEnum == ResourceTypeEnum.Assessment)
                {
                    var activity = resourceActivity.AssessmentResourceActivity.Count() > 0 ? resourceActivity.AssessmentResourceActivity.First() : null;
                    if (activity == null)
                    {
                        viewModel.Complete = false;
                        viewModel.CompletionPercentage = 0;
                        viewModel.AssessmentDetails = new MyLearningAssessmentDetails
                        {
                            PassMark = resourceActivity.ResourceVersion.AssessmentResourceVersion.PassMark,
                        };
                    }
                    else
                    {
                        viewModel.AssessmentResourceActivityId = activity.Id;
                        viewModel.Complete = activity.Score != null;
                        viewModel.ScorePercentage = activity.Score.HasValue ? (int)Math.Round(activity.Score.Value, MidpointRounding.AwayFromZero) : 0;

                        var currentAttempt = allAttempts.FindIndex(a => a.Id == resourceActivity.Id) + 1;

                        var assessmemntActivityQuestion = await this.resourceActivityRepository.GetAssessmentActivityCompletionPercentage(resourceActivity.CreateUserId, resourceActivity.ResourceVersionId, resourceActivity.Id);
                        viewModel.CompletionPercentage = (int)assessmemntActivityQuestion.CompletionPercentage;
                        if (viewModel.CompletionPercentage == 100)
                        {
                            if (resourceActivity.ResourceVersion.AssessmentResourceVersion.AssessmentType == AssessmentTypeEnum.Informal)
                            {
                                viewModel.ActivityStatus = ActivityStatusEnum.Completed;
                            }
                            else
                            {
                                viewModel.ActivityStatus = viewModel.ScorePercentage >= resourceActivity.ResourceVersion.AssessmentResourceVersion.PassMark
                                    ? ActivityStatusEnum.Passed
                                    : ActivityStatusEnum.Failed;
                            }
                        }
                        else
                        {
                            viewModel.ActivityStatus = ActivityStatusEnum.InProgress;
                        }

                        viewModel.AssessmentDetails = new MyLearningAssessmentDetails
                        {
                            ExtraAttemptReason = activity.Reason,
                            CurrentAttempt = currentAttempt,
                            MaximumAttempts = resourceActivity.ResourceVersion.AssessmentResourceVersion.MaximumAttempts,
                            PassMark = resourceActivity.ResourceVersion.AssessmentResourceVersion.PassMark,
                        };
                    }
                }

                viewModels.Add(viewModel);
            }

            return viewModels;
        }

        private IQueryable<ResourceActivity> ApplyFilters(IQueryable<ResourceActivity> query, MyLearningRequestModel requestModel)
        {
            // Text filter - Title, Keywords or Description.
            if (!string.IsNullOrEmpty(requestModel.SearchText))
            {
                query = query.Where(x => x.ResourceVersion.Title.Contains(requestModel.SearchText) ||
                                         x.ResourceVersion.Description.Contains(requestModel.SearchText) ||
                                         x.ResourceVersion.ResourceVersionKeyword.Any(y => y.Keyword.Contains(requestModel.SearchText)));
            }

            // Resource Type filter.
            if (requestModel.Article || requestModel.Audio || requestModel.Elearning || requestModel.Html || requestModel.File || requestModel.Image || requestModel.Video || requestModel.Weblink || requestModel.Assessment || requestModel.Case)
            {
                query = query.Where(x =>
                    (requestModel.Article && x.Resource.ResourceTypeEnum == ResourceTypeEnum.Article) ||
                    (requestModel.Audio && x.Resource.ResourceTypeEnum == ResourceTypeEnum.Audio) ||
                    (requestModel.Elearning && x.Resource.ResourceTypeEnum == ResourceTypeEnum.Scorm) ||
                    (requestModel.Html && x.Resource.ResourceTypeEnum == ResourceTypeEnum.Html) ||
                    (requestModel.File && x.Resource.ResourceTypeEnum == ResourceTypeEnum.GenericFile) ||
                    (requestModel.Image && x.Resource.ResourceTypeEnum == ResourceTypeEnum.Image) ||
                    (requestModel.Video && x.Resource.ResourceTypeEnum == ResourceTypeEnum.Video) ||
                    (requestModel.Weblink && x.Resource.ResourceTypeEnum == ResourceTypeEnum.WebLink) ||
                    (requestModel.Assessment && x.Resource.ResourceTypeEnum == ResourceTypeEnum.Assessment) ||
                    (requestModel.Case && x.Resource.ResourceTypeEnum == ResourceTypeEnum.Case));
            }

            // Status Filter.
            // All media activity prior to start of detailed media activity recording count as complete.
            // All media activity after start of detailed media activity recording have to be 100% complete.
            // Scorm treated as "in progress" when there is a launch event but no subsequent associated complete / passed / failed.
            // Weblink, Article, Image treated as "complete" when there is a launch event.
            if (requestModel.Complete || requestModel.Incomplete || requestModel.Passed || requestModel.Failed || requestModel.Downloaded)
            {
                query = query.Where(x =>
                (requestModel.Complete && (((x.Resource.ResourceTypeEnum == ResourceTypeEnum.Audio || x.Resource.ResourceTypeEnum == ResourceTypeEnum.Video) &&
                                            ((x.ActivityStart.Value < this.settings.Value.DetailedMediaActivityRecordingStartDate) ||
                                            (x.MediaResourceActivity != null && x.MediaResourceActivity.Any() && x.MediaResourceActivity.First().PercentComplete == 100)))
                                            ||
                                            ((x.Resource.ResourceTypeEnum == ResourceTypeEnum.WebLink || x.Resource.ResourceTypeEnum == ResourceTypeEnum.Article || x.Resource.ResourceTypeEnum == ResourceTypeEnum.Image || x.Resource.ResourceTypeEnum == ResourceTypeEnum.Case) && x.ActivityStatusId == (int)ActivityStatusEnum.Launched)
                                            ||
                                            (x.Resource.ResourceTypeEnum != ResourceTypeEnum.Audio && x.Resource.ResourceTypeEnum != ResourceTypeEnum.Video && x.ActivityStatusId == (int)ActivityStatusEnum.Completed))) ||
                (requestModel.Incomplete && (((x.Resource.ResourceTypeEnum == ResourceTypeEnum.Audio || x.Resource.ResourceTypeEnum == ResourceTypeEnum.Video) &&
                                         x.ActivityStart.Value >= this.settings.Value.DetailedMediaActivityRecordingStartDate &&
                                         (x.MediaResourceActivity == null || !x.MediaResourceActivity.Any() || x.MediaResourceActivity.First().PercentComplete < 100))
                                         ||
                                         //// Note - too slow to do this: (x.Resource.ResourceTypeEnum == ResourceTypeEnum.Scorm && x.ScormActivity.FirstOrDefault() != null && x.ScormActivity.First().CmiCoreLessonStatus == (int)ActivityStatusEnum.InProgress)
                                         //// - only returning "launched" for non-media when there is no subsequent event referencing the launch.
                                         //// (x.Resource.ResourceTypeEnum == ResourceTypeEnum.Scorm && x.ActivityStatusId == (int)ActivityStatusEnum.Launched)
                                         (x.Resource.ResourceTypeEnum == ResourceTypeEnum.Scorm && x.ScormActivity.Any() && x.ScormActivity.First().CmiCoreLessonStatus == (int)ActivityStatusEnum.InProgress)
                                         ||
                                         (x.Resource.ResourceTypeEnum == ResourceTypeEnum.Assessment && x.AssessmentResourceActivity.Any() && !x.AssessmentResourceActivity.First().Score.HasValue))) ||
                (requestModel.Passed && x.ActivityStatusId == (int)ActivityStatusEnum.Passed) ||
                (requestModel.Passed && x.Resource.ResourceTypeEnum == ResourceTypeEnum.Assessment && x.AssessmentResourceActivity.Any() && x.AssessmentResourceActivity.First().Score >= x.ResourceVersion.AssessmentResourceVersion.PassMark) ||
                (requestModel.Failed && x.ActivityStatusId == (int)ActivityStatusEnum.Failed) ||
                (requestModel.Failed && x.Resource.ResourceTypeEnum == ResourceTypeEnum.Assessment && x.AssessmentResourceActivity.Any() && x.AssessmentResourceActivity.First().Score < x.ResourceVersion.AssessmentResourceVersion.PassMark) ||
                (requestModel.Downloaded && x.ActivityStatusId == (int)ActivityStatusEnum.Downloaded));
            }

            // Date Filter.
            if (!string.IsNullOrEmpty(requestModel.TimePeriod))
            {
                var now = DateTime.Now.Date;

                if (requestModel.TimePeriod == "dateRange")
                {
                    if (requestModel.StartDate.HasValue || requestModel.EndDate.HasValue)
                    {
                        if (requestModel.StartDate.HasValue)
                        {
                            query = query.Where(x => x.ActivityStart >= requestModel.StartDate);
                        }

                        if (requestModel.EndDate.HasValue)
                        {
                            query = query.Where(x => x.ActivityStart < requestModel.EndDate.Value.AddDays(1));
                        }
                    }
                    else
                    {
                        throw new ArgumentException("If RequestModel.TimePeriod is set to 'dateRange', the RequestModel.StartDate and/or EndDate must also be specified.");
                    }
                }
                else if (requestModel.TimePeriod == "thisWeek")
                {
                    // Definition of this week is anything from the Monday prior, or today if today is Monday.
                    var firstDateOfWeek = now.FirstDateInWeek(DayOfWeek.Monday);
                    query = query.Where(x => x.ActivityStart >= firstDateOfWeek);
                }
                else if (requestModel.TimePeriod == "thisMonth")
                {
                    // Definition of this month is anything from the 1st of the current month.
                    query = query.Where(x => x.ActivityStart >= new DateTime(now.Year, now.Month, 1));
                }
                else if (requestModel.TimePeriod == "last12Months")
                {
                    // Definition of the last 12 months is anything from the same date 12 months ago. e.g. if today is 7th Oct 2020, then return anything from 7th Oct 2019.
                    query = query.Where(x => x.ActivityStart > now.AddMonths(-12));
                }
            }

            if (requestModel.CertificateEnabled)
            {
                query = query.Where(x => x.ResourceVersion.CertificateEnabled.Equals(true));
            }

            return query;
        }
    }
}