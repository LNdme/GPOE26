using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Cours.Data
{/*
    // Design-time factory used by EF tools (Update-Database, migrations) to
    // construct the DbContext when Program.cs doesn't register a direct
    // connection string for the tooling.
    public class CoursContextFactory : IDesignTimeDbContextFactory<CoursContext>
    {
        public CoursContext CreateDbContext(string[] args)
        {
            // Search for appsettings.json starting from the current directory
            // and walking up parent directories. This makes the factory robust
            // when EF tools run from the solution root or other working dirs.
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            string? basePath = null;

            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "appsettings.json");
                if (File.Exists(candidate))
                {
                    basePath = dir.FullName;
                    break;
                }
                dir = dir.Parent;
            }

            // Fall back to current directory if nothing found (will lead to
            // a clear error below if no connection string is present).
            basePath ??= Directory.GetCurrentDirectory();

            var config = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            // Try common keys used in this solution
            var connectionString = config.GetConnectionString("CoursDB")
                                    ?? config.GetConnectionString("UserDB")
                                    ?? config["ConnectionStrings:CoursDB"]
                                    ?? config["ConnectionStrings:UserDB"];

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("No connection string found. Ensure appsettings.json contains a valid ConnectionStrings entry (CoursDB or UserDB). If you run Update-Database from the Package Manager Console, set the Default project to the 'Cours' project.");

            var optionsBuilder = new DbContextOptionsBuilder<CoursContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new CoursContext(optionsBuilder.Options);
        }
    }*/
}
