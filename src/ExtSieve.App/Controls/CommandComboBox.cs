using System.Windows.Input;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ExtSieve.App.Controls;

public sealed class CommandComboBox : ComboBox
{
    public static readonly StyledProperty<ICommand?> SelectionCommittedCommandProperty =
        AvaloniaProperty.Register<CommandComboBox, ICommand?>(nameof(SelectionCommittedCommand));
    private object? _committedItem;
    private int _inputDepth;

    protected override Type StyleKeyOverride => typeof(ComboBox);

    public ICommand? SelectionCommittedCommand
    {
        get => GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    public override bool UpdateSelectionFromEvent(Control container, RoutedEventArgs eventArgs)
    {
        _inputDepth++;
        try
        {
            var selected = base.UpdateSelectionFromEvent(container, eventArgs);
            if (selected && eventArgs is PointerEventArgs or KeyEventArgs) CommitSelection();
            return selected;
        }
        finally { _inputDepth--; }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var wasOpen = IsDropDownOpen;
        _inputDepth++;
        try
        {
            base.OnKeyDown(e);
            // An open picker can preview text-search matches. Closing without a
            // committed item restores the selection represented by the binding.
            if (wasOpen && !IsDropDownOpen) RestoreCommittedSelection();
            else if (!IsDropDownOpen) CommitSelection();
        }
        finally { _inputDepth--; }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _inputDepth++;
        try
        {
            base.OnPointerWheelChanged(e);
            if (!IsDropDownOpen) CommitSelection();
        }
        finally { _inputDepth--; }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        _inputDepth++;
        try
        {
            base.OnTextInput(e);
            if (!IsDropDownOpen) CommitSelection();
        }
        finally { _inputDepth--; }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (_inputDepth == 0 && change.Property == SelectedItemProperty) _committedItem = SelectedItem;
        base.OnPropertyChanged(change);
        if (_inputDepth == 0 && change.Property == IsDropDownOpenProperty && !IsDropDownOpen)
            RestoreCommittedSelection();
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new CommandItem(this);

    private void CommitSelection()
    {
        var item = SelectedItem;
        if (item is null || ReferenceEquals(_committedItem, item)
            || !IsEffectivelyEnabled || SelectionCommittedCommand is not { } command || !command.CanExecute(item)) return;
        _committedItem = item;
        command.Execute(item);
    }

    private void RestoreCommittedSelection()
    {
        if (!ReferenceEquals(SelectedItem, _committedItem)) SelectedItem = _committedItem;
    }

    private void CommitAutomationSelection(Control container)
    {
        var index = IndexFromContainer(container);
        if (index < 0 || !IsEffectivelyEnabled || !container.IsEffectivelyEnabled) return;
        _inputDepth++;
        try
        {
            SelectedIndex = index;
            CommitSelection();
            IsDropDownOpen = false;
        }
        finally { _inputDepth--; }
    }

    private sealed class CommandItem(CommandComboBox owner) : ComboBoxItem
    {
        protected override Type StyleKeyOverride => typeof(ComboBoxItem);
        protected override AutomationPeer OnCreateAutomationPeer() => new CommandItemPeer(this, owner);
    }

    private sealed class CommandItemPeer(CommandItem item, CommandComboBox owner) : ListItemAutomationPeer(item), ISelectionItemProvider
    {
        public new void Select() => owner.CommitAutomationSelection(item);
        void ISelectionItemProvider.AddToSelection() => Select();
    }
}
