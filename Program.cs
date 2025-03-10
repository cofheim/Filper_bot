using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static ITelegramBotClient _botClient;
    private static List<Transaction> _transactions = new List<Transaction>();
    private static Dictionary<long, string> _userStates = new Dictionary<long, string>(); // Состояние пользователя

    static async Task Main(string[] args)
    {
        // Замените "YOUR_BOT_TOKEN" на токен, который вы получили от BotFather
        _botClient = new TelegramBotClient("7813170335:AAGuwuIP3hB1RGBNTf39jPoELe_-Sygm87k");

        // Запуск получения обновлений
        var cts = new CancellationTokenSource();
        _botClient.StartReceiving(UpdateHandler, ErrorHandler, cancellationToken: cts.Token);

        Console.WriteLine("Бот запущен. Нажмите Enter для выхода.");
        Console.ReadLine();

        // Остановка получения обновлений
        cts.Cancel();
    }

    private static async Task UpdateHandler(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Type != UpdateType.Message || update.Message.Type != MessageType.Text)
            return;

        var chatId = update.Message.Chat.Id;
        var messageText = update.Message.Text;

        try
        {
            // Логируем полученное сообщение
            Log($"Получено сообщение от пользователя {chatId}: {messageText}");

            // Проверяем состояние пользователя
            if (_userStates.TryGetValue(chatId, out var state))
            {
                if (decimal.TryParse(messageText, out var amount))
                {
                    if (state == "awaiting_income")
                    {
                        _transactions.Add(new Transaction { Type = "Income", Amount = amount, Date = DateTime.Now });
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"Доход на сумму {amount} руб. добавлен.",
                            replyMarkup: GetMainKeyboard(), // Возвращаем основную клавиатуру
                            cancellationToken: cancellationToken);

                        // Логируем добавление дохода
                        Log($"Добавлен доход: {amount} руб. (Пользователь: {chatId})");
                    }
                    else if (state == "awaiting_expense")
                    {
                        _transactions.Add(new Transaction { Type = "Expense", Amount = amount, Date = DateTime.Now });
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"Расход на сумму {amount} руб. добавлен.",
                            replyMarkup: GetMainKeyboard(), // Возвращаем основную клавиатуру
                            cancellationToken: cancellationToken);

                        // Логируем добавление расхода
                        Log($"Добавлен расход: {amount} руб. (Пользователь: {chatId})");
                    }

                    // Сбрасываем состояние
                    _userStates.Remove(chatId);
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Пожалуйста, введите число.",
                        cancellationToken: cancellationToken);

                    // Логируем ошибку ввода
                    Log($"Ошибка: пользователь {chatId} ввёл не число.");
                }
            }
            else
            {
                switch (messageText)
                {
                    case "/start":
                    case "Меню":
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Привет! Я бот для управления финансами. Выберите действие:",
                            replyMarkup: GetMainKeyboard(), // Основная клавиатура
                            cancellationToken: cancellationToken);

                        // Логируем запуск бота
                        Log($"Бот запущен для пользователя {chatId}.");
                        break;

                    case "Добавить доход":
                        _userStates[chatId] = "awaiting_income"; // Устанавливаем состояние
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Введите сумму дохода:",
                            replyMarkup: new ReplyKeyboardRemove(), // Убираем клавиатуру
                            cancellationToken: cancellationToken);

                        // Логируем запрос на добавление дохода
                        Log($"Пользователь {chatId} запросил добавление дохода.");
                        break;

                    case "Добавить расход":
                        _userStates[chatId] = "awaiting_expense"; // Устанавливаем состояние
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Введите сумму расхода:",
                            replyMarkup: new ReplyKeyboardRemove(), // Убираем клавиатуру
                            cancellationToken: cancellationToken);

                        // Логируем запрос на добавление расхода
                        Log($"Пользователь {chatId} запросил добавление расхода.");
                        break;

                    case "Показать баланс":
                        var balance = CalculateBalance();
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"Ваш текущий баланс: {balance} руб.",
                            replyMarkup: GetMainKeyboard(), // Возвращаем основную клавиатуру
                            cancellationToken: cancellationToken);

                        // Логируем запрос баланса
                        Log($"Пользователь {chatId} запросил баланс. Текущий баланс: {balance} руб.");
                        break;

                    case "Показать историю":
                        var history = GetTransactionHistory();
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: history,
                            replyMarkup: GetMainKeyboard(), // Возвращаем основную клавиатуру
                            cancellationToken: cancellationToken);

                        // Логируем запрос истории
                        Log($"Пользователь {chatId} запросил историю операций.");
                        break;

                    default:
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Неизвестная команда. Используйте кнопки для выбора действия.",
                            replyMarkup: GetMainKeyboard(), // Возвращаем основную клавиатуру
                            cancellationToken: cancellationToken);

                        // Логируем неизвестную команду
                        Log($"Неизвестная команда от пользователя {chatId}: {messageText}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"Произошла ошибка: {ex.Message}",
                cancellationToken: cancellationToken);

            // Логируем ошибку
            Log($"Ошибка у пользователя {chatId}: {ex.Message}");
        }
    }

    private static Task ErrorHandler(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception is ApiRequestException apiRequestException
            ? $"Telegram API Error: {apiRequestException.ErrorCode} - {apiRequestException.Message}"
            : exception.ToString();

        // Логируем ошибку
        Log($"Ошибка: {errorMessage}");
        return Task.CompletedTask;
    }

    private static decimal CalculateBalance()
    {
        return _transactions
            .Where(t => t.Type == "Income")
            .Sum(t => t.Amount) -
            _transactions
            .Where(t => t.Type == "Expense")
            .Sum(t => t.Amount);
    }

    private static string GetTransactionHistory()
    {
        if (_transactions.Count == 0)
            return "История операций пуста.";

        var history = "История операций:\n";
        foreach (var transaction in _transactions)
        {
            history += $"{transaction.Date:dd.MM.yyyy HH:mm} - {transaction.Type}: {transaction.Amount} руб.\n";
        }
        return history;
    }

    private static ReplyKeyboardMarkup GetMainKeyboard()
    {
        // Создаём клавиатуру с кнопками
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("Добавить доход"), new KeyboardButton("Добавить расход") },
            new[] { new KeyboardButton("Показать баланс"), new KeyboardButton("Показать историю") },
            new[] { new KeyboardButton("Меню") }
        })
        {
            ResizeKeyboard = true // Кнопки будут меньше по размеру
        };

        return keyboard;
    }

    private static void Log(string message)
    {
        // Выводим лог на консоль с временной меткой
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }
}

public class Transaction
{
    public string Type { get; set; } // "Income" или "Expense"
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
}