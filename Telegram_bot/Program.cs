// Program.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using test.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Добавляем сервис парсера расписания
        services.AddSingleton<ScheduleParser>();

        // Регистрируем сервис бота
        services.AddHostedService<TelegramBotService>();

        // Настройка логгирования
        services.AddLogging(configure =>
            configure.AddConsole()
                    .AddDebug()
                    .SetMinimumLevel(LogLevel.Debug));
    })
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .Build();

await host.RunAsync();