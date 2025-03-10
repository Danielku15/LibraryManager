﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.LibraryManager.Contracts;

namespace Microsoft.Web.LibraryManager
{
    /// <summary>
    /// Service to manage basic operations on libraries cache
    /// </summary>
    public class CacheService
    {
        // TO DO: Move these expirations to the provider 
        private readonly int _catalogExpiresAfterDays = 1;
        private readonly int _metadataExpiresAfterDays = 1;
        private readonly int _libraryExpiresAfterDays = 30;
        private readonly IWebRequestHandler _requestHandler;

        private static string CacheFolderValue;

        /// <summary>
        /// Instantiate the CacheService
        /// </summary>
        /// <param name="requestHandler"></param>
        public CacheService(IWebRequestHandler requestHandler)
        {
            _requestHandler = requestHandler;
        }

        /// <summary>
        /// Defines the root cache directory.  This should be consistent across all LibMan tools. 
        /// </summary>
        public static string CacheFolder
        {
            get
            {
                if (string.IsNullOrEmpty(CacheFolderValue))
                {
                    string envVar = "%HOME%";

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        envVar = "%USERPROFILE%";
                    }

                    CacheFolderValue = Path.Combine(Environment.ExpandEnvironmentVariables(envVar), ".librarymanager", "cache");
                }

                return CacheFolderValue;
            }
        }

        /// <summary>
        /// Returns the provider's catalog from the provided Url to cacheFile
        /// </summary>
        /// <param name="url"></param>
        /// <param name="cacheFile"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<string> GetCatalogAsync(string url, string cacheFile, CancellationToken cancellationToken)
        {
            return await GetResourceAsync(url, cacheFile, _catalogExpiresAfterDays, cancellationToken);
        }

        /// <summary>
        /// Returns library metadata from provided Url to cacheFile
        /// </summary>
        /// <param name="url"></param>
        /// <param name="cacheFile"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<string> GetMetadataAsync(string url, string cacheFile, CancellationToken cancellationToken)
        {
            return await GetResourceAsync(url, cacheFile, _metadataExpiresAfterDays, cancellationToken);
        }

        /// <summary>
        /// Downloads a resource from specified url to a destination file
        /// </summary>
        /// <param name="url">Url to download</param>
        /// <param name="fileName">Destination file path</param>
        /// <param name="attempts">Number of times to attempt the download</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task DownloadToFileAsync(string url, string fileName, int attempts, CancellationToken cancellationToken)
        {
            if (attempts < 1)
            {
                throw new ArgumentException("Must attempt at least one time", nameof(attempts));
            }

            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    using (Stream libraryStream = await _requestHandler.GetStreamAsync(url, cancellationToken))
                    {
                        await FileHelpers.SafeWriteToFileAsync(fileName, libraryStream, cancellationToken);
                        break;
                    }
                }
                catch (ResourceDownloadException)
                {
                    // rethrow last exception
                    if (i == attempts - 1)
                    {
                        throw;
                    }
                }

                await Task.Delay(200);
            }
        }

        private async Task<string> GetResourceAsync(string url, string localFile, int expiration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(localFile) || File.GetLastWriteTime(localFile) < DateTime.Now.AddDays(-expiration))
            {
                await DownloadToFileAsync(url, localFile, attempts: 1, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return await FileHelpers.ReadFileAsTextAsync(localFile, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Refreshes the cache for the given set of files if expired 
        /// </summary>
        /// <param name="librariesCacheMetadata"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task RefreshCacheAsync(IEnumerable<CacheFileMetadata> librariesCacheMetadata, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<Task> refreshTasks = new List<Task>();

            foreach (CacheFileMetadata metadata in librariesCacheMetadata)
            {
                if (!File.Exists(metadata.DestinationPath) || File.GetLastWriteTime(metadata.DestinationPath) < DateTime.Now.AddDays(-_libraryExpiresAfterDays))
                {
                    Task readFileTask = DownloadToFileAsync(metadata.Source, metadata.DestinationPath, attempts: 5, cancellationToken: cancellationToken);
                    await readFileTask;
                    refreshTasks.Add(readFileTask);
                }
            }

            await Task.WhenAll(refreshTasks).ConfigureAwait(false);
        }
    }
}
