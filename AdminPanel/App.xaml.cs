using System.Windows;
using AdminPanel.Database;

namespace AdminPanel;

public partial class App : Application
{
    // Глобальный доступ к БД для всего приложения
    public static DatabaseService Database { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Инициализация нативного SQLite (лечит ошибку SQLitePCL.raw.SetProvider)
        SQLitePCL.Batteries_V2.Init();

        // 2. Создание и инициализация БД (один раз при запуске)
        Database = new DatabaseService();
        Database.Initialize();

        // 3. Запуск главного окна вручную
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}