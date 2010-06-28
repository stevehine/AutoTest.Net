﻿using System;
using System.IO;
using AutoTest.Core.Messaging;
using AutoTest.Core.Configuration;
using AutoTest.Core.FileSystem.ProjectLocators;
using System.Collections.Generic;
using AutoTest.Core.FileSystem;
using Castle.Core.Logging;
using AutoTest.Core.Caching;
using AutoTest.Core.Caching.Projects;

namespace AutoTest.Core.Messaging.MessageConsumers
{
    public class FileChangeConsumer : IConsumerOf<FileChangeMessage>
    {
        private readonly IServiceLocator _services;
        private readonly IMessageBus _bus;
        private readonly ICache _cache;

        public FileChangeConsumer(IServiceLocator services, IMessageBus bus, ICache cache)
        {
            _services = services;
            _bus = bus;
            _cache = cache;
        }

        public void Consume(FileChangeMessage message)
        {
            var totalListOfProjects = new List<ChangedFile>();
            var locators = _services.LocateAll<ILocateProjects>();
            foreach (var file in message.Files)
            {
                var projects = getProjectsClosestToChangedFile(file, locators);
                combineLists(projects, totalListOfProjects);
            }
            publishProjects(totalListOfProjects);
        }

        private void publishProjects(List<ChangedFile> totalListOfProjects)
        {
            if (totalListOfProjects.Count == 0)
                return;
            var projectChange = new ProjectChangeMessage();
            projectChange.AddFile(totalListOfProjects.ToArray());
            _bus.Publish(projectChange);
        }

        private ChangedFile[] getProjectsClosestToChangedFile(ChangedFile file, ILocateProjects[] locators)
        {
            var closestProjects = new List<ChangedFile>();
            var currentLocation = 0;
            foreach (var locator in locators)
            {
                if (locator.IsProject(file.FullName))
                {
                    if (_cache.Exists(file.FullName))
                        _cache.MarkAsDirty<Project>(file.FullName);
                    else
                        _cache.Add<Project>(file.FullName);
                }

                var files = locator.Locate(file.FullName);
                if (files.Length == 0)
                    continue;
                currentLocation = addIfCloser(files, currentLocation, closestProjects);
            }
            return closestProjects.ToArray();
        }

        private int addIfCloser(ChangedFile[] suggestedProjects, int currentLocation, List<ChangedFile> closestProjects)
        {
            int location = getLocation(suggestedProjects[0]);
            if (isFertherAwayThanCurrent(location, currentLocation))
                return currentLocation;
            if (isCloserToChangedFile(location, currentLocation))
            {
                closestProjects.Clear();
                currentLocation = location;
            }
            combineLists(suggestedProjects, closestProjects);
            return currentLocation;
        }

        private bool isFertherAwayThanCurrent(int location, int currentLocation)
        {
            return location < currentLocation;
        }

        private int getLocation(ChangedFile suggestedProjects)
        {
            return suggestedProjects.FullName.Split(Path.DirectorySeparatorChar).Length;
        }

        private bool isCloserToChangedFile(int location, int currentLocation)
        {
            return location > currentLocation;
        }

        private void combineLists(ChangedFile[] source, List<ChangedFile> destination)
        {
            foreach (var changedFile in source)
                addUnique(destination, changedFile);
        }

        private void addUnique(List<ChangedFile> projectFiles, ChangedFile changedProject)
        {
            if (projectFiles.Find(x => x.FullName.Equals(changedProject.FullName)) == null)
                projectFiles.Add(changedProject);
        }
    }
}