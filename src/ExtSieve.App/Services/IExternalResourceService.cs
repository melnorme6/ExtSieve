namespace ExtSieve.App.Services;

public interface IExternalResourceService
{
    Task OpenFileAsync(string filePath);

    Task OpenUriAsync(Uri uri);
}
