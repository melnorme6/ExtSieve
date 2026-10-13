using ExtSieve.App.Localization;

namespace ExtSieve.App.ViewModels;

public abstract class LocalizedDialogViewModel : ObservableObject, IDisposable
{
    protected LocalizedDialogViewModel(LocalizationService localization)
    {
        Localization = localization;
        Localization.CultureChanged += OnCultureChanged;
    }

    protected LocalizationService Localization { get; }
    public string CancelLabel => Localization["Cancel"];
    public string CloseLabel => Localization["CloseDialog"];
    protected string Format(string key, params object[] arguments) => string.Format(Localization.CurrentCulture, Localization[key], arguments);
    private void OnCultureChanged(object? sender, EventArgs e) => RaisePropertyChanged(string.Empty);
    public virtual void Dispose()
    {
        Localization.CultureChanged -= OnCultureChanged;
        GC.SuppressFinalize(this);
    }
}
