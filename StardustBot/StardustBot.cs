using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.FormFlow;
using Microsoft.Bot.Builder.FormFlow.Json;
using MiniHack.BotApplication.Resource;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace StardustBot
{

    public enum CoffeeOptions
    {
        Coffee, CaramelMacchiato, Mocha, Chocolate, Tea
    };

    public enum TemperatureOptions
    {
        Hot, Cold
    };

    public enum SizeOptions
    {
        Ristretto, Short, Medium, Long, Big
    };

    public enum SugarOptions
    {
        ALittleBitOfSugar, ALot
    };

    public enum ToppingOptions
    {
        [Terms("except", "but", "not", "no", "all", "everything")]
        Cream, Milk, Vanilla, Chocolate, Caramel, Everything
    };

    [Serializable]
    [Template(TemplateUsage.NotUnderstood, "I do not understand \"{0}\".", "Try again, I don't get \"{0}\".")]
    [Template(TemplateUsage.EnumSelectOne, "What kind of {&} would you like on your coffee? {||}", ChoiceStyle = ChoiceStyleOptions.Buttons)]
    public class CoffeeOrder
    {
        [Prompt("What kind of {&} would you like? {||}")]
        public CoffeeOptions? Coffee;

        [Prompt("Which temperature do you want? {||}")]
        public TemperatureOptions? Temperature;

        [Prompt("What size do you want? {||}")]
        public SizeOptions? Size;

        // An optional annotation means that it is possible to not make a choice in the field.
        [Optional]
        [Prompt("Choose your toppings ? {||}")]
        [Template(TemplateUsage.NoPreference, "None")]
        public List<ToppingOptions> Toppings { get; set; }

        [Optional]
        [Prompt("Do you want some sugar ? {||}")]
        [Template(TemplateUsage.NoPreference, "None")]
        public SugarOptions? Sugar { get; set; }

        public string Name;

        public string DeliveryAddress;

        [Pattern(@"(\(\d{3}\))?\s*\d{3}(-|\s*)\d{4}")]
        public string PhoneNumber;

        [Optional]
        [Template(TemplateUsage.StatusFormat, "{&}: {:t}", FieldCase = CaseNormalization.None)]
        public DateTime? DeliveryTime;

        [Numeric(1, 5)]
        [Optional]
        [Describe("your experience today")]
        public double? Rating;


        public static IForm<JObject> BuildJsonForm()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MiniHack.BotApplication.CoffeeBot.json"))
            {
                var schema = JObject.Parse(new StreamReader(stream).ReadToEnd());
                return new FormBuilderJson(schema)
                    .AddRemainingFields()
                    .Build();
            }
        }

        private static ConcurrentDictionary<CultureInfo, IForm<CoffeeOrder>> _forms = new ConcurrentDictionary<CultureInfo, IForm<CoffeeOrder>>();

        public static IForm<CoffeeOrder> BuildLocalizedForm()
        {
            var culture = Thread.CurrentThread.CurrentUICulture;
            IForm<CoffeeOrder> form;

            if (!_forms.TryGetValue(culture, out form))
            {
                OnCompletionAsyncDelegate<CoffeeOrder> processOrder = async (context, state) =>
                {
                    await context.PostAsync(DynamicStardust.Processing);
                };

                var builder = new FormBuilder<CoffeeOrder>()
                        .Message(DynamicStardust.WelcomeMessage)
                        .Field(nameof(Coffee))
                        .Field(nameof(Temperature))
                        .Field(nameof(Size))
                        .Field(nameof(Sugar))
                        .Field(nameof(Toppings),
                            validate: async (state, value) =>
                            {
                                if (value != null)
                                {
                                    var values = ((List<object>)value).OfType<ToppingOptions>();
                                    var result = new ValidateResult { IsValid = true, Value = values };
                                    if (values != null && values.Contains(ToppingOptions.Everything))
                                    {
                                        result.Value = (from ToppingOptions topping in Enum.GetValues(typeof(ToppingOptions))
                                                        where topping != ToppingOptions.Everything && !values.Contains(topping)
                                                        select topping).ToList();
                                    }
                                    return result;
                                }
                                else
                                {
                                    // To handle null value when choosing none in toppings
                                    var result = new ValidateResult { IsValid = true, Value = null };
                                    return result;
                                }
                            })
                        .Confirm(async (state) =>
                        {
                            var cost = 0.0;
                            switch (state.Size)
                            {
                                case SizeOptions.Ristretto: cost = 2.0; break;
                                case SizeOptions.Short: cost = 3.49; break;
                                case SizeOptions.Medium: cost = 5.0; break;
                                case SizeOptions.Long: cost = 6.49; break;
                                case SizeOptions.Big: cost = 8.99; break;
                            }
                            return new PromptAttribute($"Total for your coffee is {cost:C2} is that ok?");
                        })
                        .Confirm(async (state) =>
                        {
                            string customMessage = "Do you want to order your {Temperature} {Size} {Coffee} ";
                            if (state.Sugar != null || state.Toppings != null)
                            {
                                customMessage = string.Concat(customMessage, "with {[{Sugar} {Toppings}]} ");
                            }
                            customMessage = string.Concat(customMessage, "?");
                            return new PromptAttribute(customMessage);
                        })
                        //.AddRemainingFields() // Pour aller plus loin
                        .Message("Thanks for ordering a coffee!")
                        .OnCompletion(processOrder);

                builder.Configuration.DefaultPrompt.ChoiceStyle = ChoiceStyleOptions.Auto;
                form = builder.Build();
                _forms[culture] = form;
            }
            return form;
        }
    }
}