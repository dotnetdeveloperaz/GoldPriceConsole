﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using MetalPriceConsole.Models;
using MySqlConnector;
using MetalPriceConsole.Commands.Settings;
using System.ComponentModel;
using System.Data;
using System.Reflection.Metadata;
using System.Runtime.Intrinsics.X86;

namespace MetalPriceConsole.Commands;

public class MissingCommand(ApiServer apiServer, ConnectionStrings ConnectionString) : AsyncCommand<MissingCommand.Settings>
{
    private readonly string _connectionString = ConnectionString.DefaultDB;
    private readonly ApiServer _apiServer = apiServer;
    private string metal;
    private string metalName;
    private static readonly string[] columns = new[] { "" };

    public class Settings : BaseCommandSettings
    {
        [CommandOption("--db")]
        [Description("Override Configured DB For Testing")]
        [DefaultValue(null)]
        public string DBConnectionString { get; set; }

        [CommandOption("--gold")]
        [Description("Get Gold Price - This is the default and is optional")]
        [DefaultValue(false)]
        public bool GetGold { get; set; }

        [CommandOption("--silver")]
        [Description("Get Silver Price")]
        [DefaultValue(false)]
        public bool GetSilver {  get; set; }

        [CommandOption("--start <date>")]
        [Description("Date Or Start Date To Get Price(s) For")]
        [DefaultValue(null)]
        public string StartDate { get; set; }

        [CommandOption("--end <date>")]
        [Description("End Date To Get Price(s) For - Not Required For Single Day")]
        [DefaultValue(null)]
        public string EndDate { get; set; }

        /*  These two metals do not currently support historic data so disabling
                [CommandOption("--palladium")]
                [Description("Get Palladium Price")]
                [DefaultValue(false)]
                public bool GetPalladium { get; set; }

                [CommandOption("--platinum")]
                [Description("Get Platinum Price")]
                [DefaultValue(false)]
                public bool GetPlatinum { get; set; }
         */
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.DBConnectionString ??= _connectionString;
        settings.StartDate ??= _apiServer.HistoricalStartDate;
        settings.EndDate ??= DateTime.Now.ToString("yyyy-MM-dd");

        if (settings.GetSilver)
        {
            metal = "XAG";
            metalName = "Silver";
            settings.GetGold = true;
        }
        else
        {
            metal = "XAU";
            metalName = "Gold";
            settings.GetGold = true;
        }
        if (settings.Debug)
        {
            if (!DebugDisplay.Print(settings, _apiServer, _connectionString, "N/A"))
                return 0;

        }

        // Process Window
        var table = new Table().Centered();
        table.HideHeaders();
        table.BorderColor(Color.Green3);
        table.Border(TableBorder.Rounded);
        table.AddColumns(columns);
        table.Expand();
        table.Columns[0].Centered();

        // Animate
        await AnsiConsole
            .Live(table)
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Top)
            .StartAsync(async ctx =>
            {
                void Update(int delay, Action action)
                {
                    action();
                    ctx.Refresh();
                    Thread.Sleep(delay);
                }

                Update(
                    70,
                    () =>
                        table.AddRow(
                            $"[green bold]Retrieving {metalName} Missing Data Information[/]"
                        )
                );
                // Content
                Update(70, () => table.Columns[0].Footer($"[green]Querying Database For Missing {metalName} Data....[/]"));
                var conn = new MySqlConnection(settings.DBConnectionString);
                var sqlCommand = new MySqlCommand
                {
                    Connection = conn,
                    CommandType = CommandType.StoredProcedure,
                    CommandText = "usp_GetMetalMissing"
                };
                try
                {
                    await conn.OpenAsync();
                    sqlCommand.Parameters.AddWithValue("metalName", metal);
                    sqlCommand.Parameters.AddWithValue("startDate", settings.StartDate);
                    sqlCommand.Parameters.AddWithValue("endDate", settings.EndDate);
                    var reader = await sqlCommand.ExecuteReaderAsync();
                    int cnt = 0;
                    while (reader.Read())
                    {
                        Update(70, () => table.AddRow($"{DateTime.Parse(reader[0].ToString()).ToShortDateString()}")); 
                        cnt++;
                    }
                    Update(70, () => table.AddRow($"[green]Retrieved {cnt} Records[/]"));
                    
                    await reader.CloseAsync();
                    await reader.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Update(70, () => table.AddRow($"[red]Request Error: {ex.Message}[/]"));
                    return;
                }
                finally
                {
                    Update(70, () => table.AddRow($"[green bold]Cleaning up...[/]"));
                    sqlCommand.Dispose();
                    if (conn.State == System.Data.ConnectionState.Open)
                        await conn.CloseAsync();
                    await conn.DisposeAsync();
                }

                Update(70, () => table.Columns[0].Footer($"[blue]Finished Retrieving {metalName} Missing Data Information[/]"));
            });
        return 0;
    }
}
