using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AdminPanel.Database;
using AdminPanel.Models;
using AdminPanel.Networking;

namespace AdminPanel.ViewModels;

public sealed class AdminViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _db;
    private readonly WebSocketServer _server;

    // --- Коллекции для UI ---
    public ObservableCollection<AgentRecord> Agents { get; } = new();
    public ObservableCollection<ComponentItem> SelectedAgentComponents { get; } = new();

    // --- Свойства ---
    private AgentRecord? _selectedAgent;
    public AgentRecord? SelectedAgent
    {
        get => _selectedAgent;
        set
        {
            _selectedAgent = value;
            OnPropertyChanged();
            LoadAgentComponents(value?.NodeId);
        }
    }

    private string _status = "Остановлен";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    private string _agentUrl = "Нажмите «Запустить», чтобы получить ссылку";
    public string AgentUrl
    {
        get => _agentUrl;
        set { _agentUrl = value; OnPropertyChanged(); }
    }

    private bool _isRunning;

    // --- Команды ---
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand CopyUrlCommand { get; }
    public ICommand ClearDbCommand { get; }

    public AdminViewModel(DatabaseService db)
    {
        _db = db;

        _server = new WebSocketServer(_db);
        _server.AgentConnected += OnAgentConnected;
        _server.Error += msg => UpdateStatus($"Ошибка: {msg}");

        StartCommand = new RelayCommand(_ => Start(), _ => !_isRunning);
        StopCommand = new RelayCommand(_ => Stop(), _ => _isRunning);

        CopyUrlCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrWhiteSpace(AgentUrl) && _isRunning)
                Clipboard.SetText(AgentUrl);
        }, _ => _isRunning);

        ClearDbCommand = new RelayCommand(_ => ClearDatabase());

        LoadAgents();
    }

    // --- Управление сервером ---
    private void Start()
    {
        try
        {
            _server.Start();
            _isRunning = true;

            // Получаем локальный IPv4 адрес компьютера
            string localIp = "localhost";
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ip != null) localIp = ip.ToString();
            }
            catch { /* Если не удалось, останется localhost */ }

            AgentUrl = $"ws://{localIp}:9000/";
            UpdateStatus("Слушаю порт 9000");
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось запустить сервер: {ex.Message}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Stop()
    {
        _server.Stop();
        _isRunning = false;
        AgentUrl = "Нажмите «Запустить», чтобы получить ссылку";
        UpdateStatus("Остановлен");
        CommandManager.InvalidateRequerySuggested();
    }

    // --- Загрузка данных ---
    private void OnAgentConnected()
    {
        Application.Current.Dispatcher.Invoke(LoadAgents);
    }

    private void LoadAgents()
    {
        var previousId = _selectedAgent?.NodeId;

        Agents.Clear();
        foreach (var a in _db.GetAllAgents())
            Agents.Add(a);

        if (previousId is not null)
            SelectedAgent = Agents.FirstOrDefault(a => a.NodeId == previousId);
    }

    private void LoadAgentComponents(string? nodeId)
    {
        SelectedAgentComponents.Clear();
        if (string.IsNullOrWhiteSpace(nodeId)) return;

        foreach (var item in _db.GetLatestComponents(nodeId))
        {
            SelectedAgentComponents.Add(item);
        }
    }

    // --- Очистка БД ---
    private void ClearDatabase()
    {
        var result = MessageBox.Show(
            "Вы уверены, что хотите полностью очистить базу данных?\nВсе сохраненные агенты и их комплектующие будут удалены безвозвратно.",
            "Подтверждение очистки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                _db.ClearAllData();

                // Очищаем интерфейс
                SelectedAgent = null;
                LoadAgents(); // Перезагружаем пустой список

                UpdateStatus("База данных успешно очищена");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при очистке БД: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // --- Вспомогательные методы ---
    private void UpdateStatus(string msg)
    {
        if (Application.Current.Dispatcher.CheckAccess())
            Status = msg;
        else
            Application.Current.Dispatcher.Invoke(() => Status = msg);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}