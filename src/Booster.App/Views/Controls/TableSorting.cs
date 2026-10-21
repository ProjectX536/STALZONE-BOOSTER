using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Booster.Core.Reports;

namespace Booster.App.Views.Controls;

public static class TableSorting
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(TableSorting), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(SortState), typeof(TableSorting), new PropertyMetadata(null));

    private static readonly DependencyProperty ResetHandlerProperty = DependencyProperty.RegisterAttached(
        "ResetHandler", typeof(NotifyCollectionChangedEventHandler), typeof(TableSorting), new PropertyMetadata(null));

    public static bool GetIsEnabled(DataGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return (bool)grid.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DataGrid grid, bool value)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.SetValue(IsEnabledProperty, value);
    }

    internal static void SortBy(DataGrid grid, DataGridColumn column)
    {
        var index = grid.Columns.IndexOf(column);
        if (index < 0)
            return;

        var descending = grid.GetValue(StateProperty) is SortState { Descending: false } current && current.Column == index;
        grid.SetValue(StateProperty, new SortState(index, descending));
        Apply(grid);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
            return;

        var items = (INotifyCollectionChanged)grid.Items;
        grid.Sorting -= OnSorting;
        if (grid.GetValue(ResetHandlerProperty) is NotifyCollectionChangedEventHandler previous)
            items.CollectionChanged -= previous;
        grid.ClearValue(ResetHandlerProperty);
        if (e.NewValue is not true)
            return;

        grid.CanUserSortColumns = true;
        grid.Sorting += OnSorting;
        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
                Apply(grid);
        };
        grid.SetValue(ResetHandlerProperty, handler);
        items.CollectionChanged += handler;
    }

    private static void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        SortBy((DataGrid)sender, e.Column);
    }

    private static void Apply(DataGrid grid)
    {
        if (grid.GetValue(StateProperty) is not SortState state || state.Column >= grid.Columns.Count)
            return;

        var column = grid.Columns[state.Column];
        foreach (var other in grid.Columns)
            other.SortDirection = ReferenceEquals(other, column)
                ? state.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending
                : null;

        if (CollectionViewSource.GetDefaultView(grid.ItemsSource) is not ListCollectionView view
            || view.CustomSort is ColumnComparer existing && existing.State == state && existing.Column == column)
            return;

        view.CustomSort = new ColumnComparer(state, column, Path(column), CultureInfo.CurrentCulture);
    }

    private static string? Path(DataGridColumn column) => !string.IsNullOrEmpty(column.SortMemberPath) ? column.SortMemberPath
        : column is DataGridBoundColumn { Binding: Binding binding } ? binding.Path?.Path
        : null;

    private sealed record SortState(int Column, bool Descending);

    private sealed class ColumnComparer(SortState state, DataGridColumn column, string? path, CultureInfo culture) : IComparer
    {
        public SortState State => state;

        public DataGridColumn Column => column;

        public int Compare(object? x, object? y) => TableOrder.Compare(Value(x), Value(y), state.Descending, culture);

        private object? Value(object? item) => item switch
        {
            ReportRow row => state.Column < row.Cells.Count ? row.Cells[state.Column] : null,
            null => null,
            _ when string.IsNullOrEmpty(path) => null,
            _ => Member(item, path),
        };

        private static object? Member(object item, string path)
        {
            object? current = item;
            foreach (var name in path.Split('.'))
            {
                if (current is null)
                    return null;
                current = current.GetType().GetProperty(name)?.GetValue(current);
            }
            return current;
        }
    }
}
