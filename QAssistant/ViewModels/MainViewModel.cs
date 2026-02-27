// Copyright (C) 2026 Lewandowskista
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QAssistant.Services;
using QAssistant.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace QAssistant.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly StorageService _storage = StorageService.Instance;

        [ObservableProperty]
        public partial ObservableCollection<Project> Projects { get; set; } = [];


        [ObservableProperty]
        public partial Project? SelectedProject { get; set; }

        [ObservableProperty]
        public partial string ActiveTab { get; set; } = "Links";

        /// <summary>
        /// Set by notification click to signal TasksPage to auto-open this task's sidebar.
        /// </summary>
        public Guid? PendingTaskId { get; set; }

        public async Task InitializeAsync()
        {
            try
            {
                var loaded = await _storage.LoadProjectsAsync();
                Projects = new ObservableCollection<Project>(loaded);

                if (Projects.Count == 0)
                {
                    var demo = new Project { Name = "My QA Project" };
                    demo.Links.Add(new EmbedLink { Title = "Notion Workspace", Url = "https://notion.so", Type = LinkType.Notion });
                    demo.Links.Add(new EmbedLink { Title = "Figma Designs", Url = "https://figma.com", Type = LinkType.Figma });
                    demo.Links.Add(new EmbedLink { Title = "Linear Board", Url = "https://linear.app", Type = LinkType.Linear });
                    demo.Links.Add(new EmbedLink { Title = "GitHub Repo", Url = "https://github.com", Type = LinkType.GitHub });
                    Projects.Add(demo);
                    await SaveAsync();
                }

                SelectedProject = Projects.Count > 0 ? Projects[0] : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainViewModel.InitializeAsync error: {ex.Message}");
                throw;
            }
        }

        [RelayCommand]
        private async Task AddProject()
        {
            var p = new Project { Name = $"Project {Projects.Count + 1}" };
            Projects.Add(p);
            SelectedProject = p;
            await SaveAsync();
        }

        [RelayCommand]
        private void SelectTab(string tab) => ActiveTab = tab;

        public event Action<Exception>? SaveFailed;

        public async Task SaveAsync()
        {
            try
            {
                await _storage.SaveProjectsAsync(new List<Project>(Projects));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainViewModel.SaveAsync error: {ex.Message}");
                SaveFailed?.Invoke(ex);
            }
        }
    }
}