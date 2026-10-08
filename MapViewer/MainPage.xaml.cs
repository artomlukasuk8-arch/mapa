namespace MapViewer;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        StatusLabel.Text = "Готово. Додаток буде завантажувати manifest.json з GitHub.";
    }

    private async void RefreshClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = "Перевірка оновлення карти...";
        await Task.Delay(500);
        StatusLabel.Text = "Карта не знайдена локально. Потребує URL-репозиторію GitHub.";
    }

    private async void PanoramaClicked(object sender, EventArgs e)
    {
        await DisplayAlert("360 панорама", "У майбутньому тут буде 360-фото та інтерфейс для обертання виду.", "OK");
    }
}
