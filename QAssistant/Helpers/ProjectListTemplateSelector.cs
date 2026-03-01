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

using QAssistant.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QAssistant.Helpers
{
    /// <summary>
    /// Selects between the project row template and the client group-header template
    /// when rendering items in the Projects sidebar <see cref="ListView"/>.
    /// </summary>
    public sealed class ProjectListTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? ProjectTemplate { get; set; }
        public DataTemplate? GroupHeaderTemplate { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item) =>
            item is ProjectGroupHeader ? GroupHeaderTemplate : ProjectTemplate;

        protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
            SelectTemplateCore(item);
    }
}
