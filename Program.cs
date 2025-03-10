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

    // Категории доходов и расходов
    private static readonly List<string> IncomeCategories = new List<string> { "Зарплата", "Подарок", "Инвестиции", "Прочее" };
    private static readonly List<string> ExpenseCategories = new List<string> { "Коммунальные услуги", "Продукты", "Развлечения", "Транспорт", "Прочее" };

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

            // Обработка команды "Меню" в любом состоянии
            if (messageText == "Меню")
            {
                await ShowMainMenu(chatId, cancellationToken);
                return;
            }

            // Проверяем состояние пользователя
            if (_userStates.TryGetValue(chatId, out var state))
            {
                if (state.StartsWith("awaiting_category"))
                {
                    if (messageText == "Назад")
                    {
                        await ShowMainMenu(chatId, cancellationToken);
                        return;
                    }

                    var transactionType = state.Split('_')[2]; // "income" или "expense"
                    var category = messageText;

                    // Проверяем категорию с учётом регистра и пробелов
                    var categories = transactionType == "income" ? IncomeCategories : ExpenseCategories;
                    var cleanCategory = categories.FirstOrDefault(c => c.Trim().Equals(messageText.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (cleanCategory != null)
                    {
                        _userStates[chatId] = $"awaiting_amount_{transactionType}_{cleanCategory}";
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Введите сумму:",
                            replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Назад") }) { ResizeKeyboard = true },
                            cancellationToken: cancellationToken);

                        // Логируем выбор категории
                        Log($"Пользователь {chatId} выбрал категорию: {cleanCategory} ({transactionType})");
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Неверная категория. Попробуйте ещё раз.",
                            replyMarkup: GetCategoriesKeyboard(transactionType),
                            cancellationToken: cancellationToken);

                        // Логируем ошибку выбора категории
                        Log($"Ошибка: пользователь {chatId} выбрал неверную категорию: {category}");
                    }
                }
                else if (state.StartsWith("awaiting_amount"))
                {
                    if (messageText == "Назад")
                    {
                        var transactionType = state.Split('_')[2]; // "income" или "expense"
                        _userStates[chatId] = $"awaiting_category_{transactionType}";
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Выберите категорию:",
                            replyMarkup: GetCategoriesKeyboard(transactionType),
                            cancellationToken: cancellationToken);
                        return;
                    }

                    if (decimal.TryParse(messageText, out var amount))
                    {
                        var parts = state.Split('_');
                        var transactionType = parts[2]; // "income" или "expense"
                        var category = parts[3];

                        _transactions.Add(new Transaction
                        {
                            Type = transactionType == "income" ? "Income" : "Expense",
                            Category = category,
                            Amount = amount,
                            Date = DateTime.Now
                        });

                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"{(transactionType == "income" ? "Доход" : "Расход")} на сумму {amount} руб. (категория: {category}) добавлен.",
                            replyMarkup: GetMainKeyboard(), // Возвращаем основную клавиатуру
                            cancellationToken: cancellationToken);

                        // Логируем добавление транзакции
                        Log($"Добавлен {(transactionType == "income" ? "доход" : "расход")}: {amount} руб. (категория: {category}, пользователь: {chatId})");

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
            }
            else
            {
                switch (messageText)
                {
                    case "/start":
                    case "Меню":
                        await ShowMainMenu(chatId, cancellationToken);
                        break;

                    case "Добавить доход":
                        _userStates[chatId] = "awaiting_category_income"; // Устанавливаем состояние
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Выберите категорию дохода:",
                            replyMarkup: GetCategoriesKeyboard("income"), // Клавиатура с категориями доходов
                            cancellationToken: cancellationToken);

                        // Логируем запрос на добавление дохода
                        Log($"Пользователь {chatId} запросил добавление дохода.");
                        break;

                    case "Добавить расход":
                        _userStates[chatId] = "awaiting_category_expense"; // Устанавливаем состояние
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Выберите категорию расхода:",
                            replyMarkup: GetCategoriesKeyboard("expense"), // Клавиатура с категориями расходов
                            cancellationToken: cancellationToken);

                        // Логируем запрос на добавление расхода
                        Log($"Пользователь {chatId} запросил добавление расхода.");
                        break;

                    case "Показать баланс":
                        var balance = CalculateBalance();
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"Ваш текущий баланс:\n{balance}",
                            replyMarkup: GetMainKeyboard(), // Возвращаем основную клавиатуру
                            cancellationToken: cancellationToken);

                        // Логируем запрос баланса
                        Log($"Пользователь {chatId} запросил баланс.");
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

    private static string CalculateBalance()
    {
        var incomeByCategory = _transactions
            .Where(t => t.Type == "Income")
            .GroupBy(t => t.Category)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var expenseByCategory = _transactions
            .Where(t => t.Type == "Expense")
            .GroupBy(t => t.Category)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var totalIncome = incomeByCategory.Values.Sum();
        var totalExpense = expenseByCategory.Values.Sum();
        var balance = totalIncome - totalExpense;

        var result = "Доходы по категориям:\n";
        foreach (var category in incomeByCategory)
        {
            result += $"{category.Key}: {category.Value} руб.\n";
        }

        result += "\nРасходы по категориям:\n";
        foreach (var category in expenseByCategory)
        {
            result += $"{category.Key}: {category.Value} руб.\n";
        }

        result += $"\nОбщий баланс: {balance} руб.";
        return result;
    }

    private static string GetTransactionHistory()
    {
        if (_transactions.Count == 0)
            return "История операций пуста.";

        var history = "История операций:\n";
        foreach (var transaction in _transactions)
        {
            history += $"{transaction.Date:dd.MM.yyyy HH:mm} - {transaction.Type} ({transaction.Category}): {transaction.Amount} руб.\n";
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

    private static ReplyKeyboardMarkup GetCategoriesKeyboard(string type)
    {
        var categories = type == "income" ? IncomeCategories : ExpenseCategories;
        var buttons = new List<KeyboardButton[]>();

        foreach (var category in categories)
        {
            buttons.Add(new[] { new KeyboardButton(category) });
        }
        buttons.Add(new[] { new KeyboardButton("Назад"), new KeyboardButton("Меню") });

        return new ReplyKeyboardMarkup(buttons) { ResizeKeyboard = true };
    }

    private static async Task ShowMainMenu(long chatId, CancellationToken cancellationToken)
    {
        _userStates.Remove(chatId);
        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Главное меню:",
            replyMarkup: GetMainKeyboard(),
            cancellationToken: cancellationToken);
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
    public string Category { get; set; } // Категория
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
}