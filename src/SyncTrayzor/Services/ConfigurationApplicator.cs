﻿using SyncTrayzor.NotifyIcon;
using SyncTrayzor.Properties;
using SyncTrayzor.Services.UpdateChecker;
using SyncTrayzor.SyncThing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncTrayzor.Services
{
    public class ConfigurationApplicator
    {
        private readonly IConfigurationProvider configurationProvider;

        private readonly INotifyIconManager notifyIconManager;
        private readonly ISyncThingManager syncThingManager;
        private readonly IAutostartProvider autostartProvider;
        private readonly IWatchedFolderMonitor watchedFolderMonitor;
        private readonly IGithubApiClient githubApiClient;
        private readonly IUpdateChecker updateChecker;

        public ConfigurationApplicator(
            IConfigurationProvider configurationProvider,
            INotifyIconManager notifyIconManager,
            ISyncThingManager syncThingManager,
            IAutostartProvider autostartProvider,
            IWatchedFolderMonitor watchedFolderMonitor,
            IGithubApiClient githubApiClient,
            IUpdateChecker updateChecker)
        {
            this.configurationProvider = configurationProvider;
            this.configurationProvider.ConfigurationChanged += (o, e) => this.ApplyNewConfiguration(e.NewConfiguration);

            this.notifyIconManager = notifyIconManager;
            this.syncThingManager = syncThingManager;
            this.autostartProvider = autostartProvider;
            this.watchedFolderMonitor = watchedFolderMonitor;
            this.githubApiClient = githubApiClient;
            this.updateChecker = updateChecker;

            // Do this before signing up for DataLoaded, so any changes we make don't trigger us again
            this.UpdateConfigOnInit();

            this.syncThingManager.DataLoaded += (o, e) => this.LoadFolders();
            this.updateChecker.VersionIgnored += (o, e) =>
            {
                var config = this.configurationProvider.Load();
                config.LatestNotifiedVersion = e.IgnoredVersion;
                this.configurationProvider.Save(config);
            };
        }

        private void UpdateConfigOnInit()
        {
            this.UpdateAutostart();
        }

        private void UpdateAutostart()
        {
            // Don't have permission? Meh
            if (!this.autostartProvider.CanRead)
                return;

            // If the user's manually updated the registry themselves, update our config to match
            var config = this.configurationProvider.Load();
            var autostartConfig = this.autostartProvider.GetCurrentSetup();
            // We only know enough to change StartMinimized if autostartConfig.AutoStart is strue
            if (config.StartOnLogon != autostartConfig.AutoStart || (autostartConfig.AutoStart && config.StartMinimized != autostartConfig.StartMinimized))
            {
                if (autostartConfig.AutoStart)
                    config.StartMinimized = autostartConfig.StartMinimized;
                config.StartOnLogon = autostartConfig.AutoStart;
                this.configurationProvider.Save(config);
            }
        }

        public void ApplyConfiguration()
        {
            this.githubApiClient.SetConnectionDetails(Settings.Default.GithubApiUrl);
            this.watchedFolderMonitor.BackoffInterval = TimeSpan.FromMilliseconds(Settings.Default.DirectoryWatcherBackoffMilliseconds);
            this.watchedFolderMonitor.FolderExistenceCheckingInterval = TimeSpan.FromMilliseconds(Settings.Default.DirectoryWatcherFolderExistenceCheckMilliseconds);

            this.ApplyNewConfiguration(this.configurationProvider.Load());
        }

        private void ApplyNewConfiguration(Configuration configuration)
        {
            this.notifyIconManager.CloseToTray = configuration.CloseToTray;
            this.notifyIconManager.ShowOnlyOnClose = configuration.ShowTrayIconOnlyOnClose;
            this.notifyIconManager.ShowSynchronizedBalloon = configuration.ShowSynchronizedBalloon;

            this.syncThingManager.Address = new Uri(configuration.SyncthingAddress);
            this.syncThingManager.ExecutablePath = configuration.SyncthingPath;
            this.syncThingManager.ApiKey = configuration.SyncthingApiKey;
            this.syncThingManager.SyncthingTraceFacilities = configuration.SyncthingTraceFacilities;

            // Debug builds never set autostart
            // Don't have permission? Meh
            if (this.autostartProvider.CanWrite)
                this.autostartProvider.SetAutoStart(new AutostartConfiguration() { AutoStart = configuration.StartOnLogon, StartMinimized = configuration.StartMinimized });

            this.watchedFolderMonitor.WatchedFolderIDs = configuration.Folders.Where(x => x.IsWatched).Select(x => x.ID);

            this.updateChecker.LatestIgnoredVersion = configuration.LatestNotifiedVersion;
        }

        private void LoadFolders()
        {
            var configuration = this.configurationProvider.Load();
            var folderIds = this.syncThingManager.FetchAllFolders().Select(x => x.FolderId).ToList();

            foreach (var newKey in folderIds.Except(configuration.Folders.Select(x => x.ID)))
            {
                configuration.Folders.Add(new FolderConfiguration(newKey, true));
            }

            configuration.Folders = configuration.Folders.Where(x => folderIds.Contains(x.ID)).ToList();

            this.configurationProvider.Save(configuration);
        }
    }
}
