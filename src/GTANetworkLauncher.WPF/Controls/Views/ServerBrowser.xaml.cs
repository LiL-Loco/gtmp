using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GrandTheftMultiplayer.Launcher.Helpers;
using ServerModel = GrandTheftMultiplayer.Launcher.Models.ServerApi.Server;

namespace GrandTheftMultiplayer.Launcher.Controls.Views
{
    public partial class ServerBrowser
    {
        /// <summary>Smallest the name column may become before text is clipped.</summary>
        private const double MinimumNameColumnWidth = 180;

        public ServerBrowser()
        {
            InitializeComponent();
        }

        private void ServersListViewOnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;

            var listView = sender as ListView;
            if (listView != null) listView.SelectedItem = null;
        }

        private void ServersListViewOnLoaded(object sender, RoutedEventArgs e)
        {
            GridViewSort.ApplySort(ServersListView.Items, nameof(ServerModel.PlayerCount), ServersListView, PlayerCountColumnHeader);

            FitNameColumn();
        }

        private void ServersListViewOnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            FitNameColumn();
        }

        /// <summary>
        /// Gives the name column whatever width the other columns leave over.
        /// </summary>
        /// <remarks>
        /// GridViewColumn.Width is a plain double, not a GridLength, so the star
        /// syntax that does this in a Grid is not available here. With the released
        /// fixed widths the columns added up to more than the window, and the
        /// GameMode column fell off the right edge instead of shrinking.
        /// <para>
        /// Manual resizing of this column is overwritten on the next resize; the
        /// alternative is tracking header drags, which is more machinery than the
        /// behaviour is worth.
        /// </para>
        /// </remarks>
        private void FitNameColumn()
        {
            var gridView = ServersListView.View as GridView;
            if (gridView == null) return;

            // Vertical scrollbar plus a little breathing room, so the last column
            // does not sit flush against the window edge.
            var available = ServersListView.ActualWidth - SystemParameters.VerticalScrollBarWidth - 8;
            if (available <= 0) return;

            var usedByOthers = 0d;
            foreach (var column in gridView.Columns)
            {
                if (!ReferenceEquals(column, ServerNameColumn)) usedByOthers += column.ActualWidth;
            }

            var target = Math.Max(MinimumNameColumnWidth, available - usedByOthers);

            ServerNameColumn.Width = target;
        }
    }
}
