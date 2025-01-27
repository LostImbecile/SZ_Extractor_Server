﻿using CommandLine;

namespace UE_Extractor
{
    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(options =>
                {
                    try
                    {
                        options.Validate();
                        new Extractor(options).Run();
                    }
                    catch (ArgumentException ex)
                    {
                        Console.Error.WriteLine($"Error: {ex.Message}");
                        Environment.Exit(1);
                    }
                });
        }
    }
}