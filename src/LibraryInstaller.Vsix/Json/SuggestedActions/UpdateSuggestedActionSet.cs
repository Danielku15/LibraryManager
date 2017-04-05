﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using LibraryInstaller.Contracts;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.Web.Editor.SuggestedActions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryInstaller.Vsix
{
    internal class UpdateSuggestedActionSet : SuggestedActionBase
    {
        private static readonly Guid _guid = new Guid("2975f71b-809a-4ed6-a170-6bbc04058424");
        private SuggestedActionProvider _provider;

        public UpdateSuggestedActionSet(SuggestedActionProvider provider)
            : base(provider.TextBuffer, provider.TextView, Resources.Text.CheckForUpdates, _guid)
        {
            _provider = provider;
        }

        public override bool HasActionSets => true;

        public override async Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
        {
            var dependencies = Dependencies.FromConfigFile(_provider.ConfigFilePath);
            IProvider provider = dependencies.GetProvider(_provider.InstallationState.ProviderId);
            ILibraryCatalog catalog = provider?.GetCatalog();

            if (catalog == null)
            {
                return null;
            }

            try
            {
                return await GetActionSetAsync(catalog, cancellationToken);
            }
            catch (Exception ex)
            {
                Telemetry.TrackException(nameof(GetActionSetsAsync), ex);
                return null;
            }
        }

        private async Task<IEnumerable<SuggestedActionSet>> GetActionSetAsync(ILibraryCatalog catalog, CancellationToken cancellationToken)
        {
            var list = new List<ISuggestedAction>();

            string latestStable = await catalog.GetLatestVersion(_provider.InstallationState.LibraryId, false, cancellationToken);

            if (!string.IsNullOrEmpty(latestStable) && latestStable != _provider.InstallationState.LibraryId)
            {
                list.Add(new UpdateSuggestedAction(_provider, latestStable, $"Stable: {latestStable}"));
            }

            string latestPre = await catalog.GetLatestVersion(_provider.InstallationState.LibraryId, true, cancellationToken);

            if (!string.IsNullOrEmpty(latestPre) && latestPre != _provider.InstallationState.LibraryId && latestPre != latestStable)
            {
                list.Add(new UpdateSuggestedAction(_provider, latestPre, $"Pre-release: {latestPre}"));
            }

            if (list.Count == 0)
            {
                list.Add(new UpdateSuggestedAction(_provider, null, "No updates found", true));
            }

            return new[] { new SuggestedActionSet(list, "Update library") };
        }

        public override void Invoke(CancellationToken cancellationToken)
        {

        }
    }
}
