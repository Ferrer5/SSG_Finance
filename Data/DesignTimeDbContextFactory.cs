using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyMvcApp.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Compose from DB_* env vars, fall back to ConnectionStrings__DefaultConnection
        var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
        string connectionString;
        if (!string.IsNullOrEmpty(dbHost))
        {
            var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
            var dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? "ssg_system";
            var dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "root";
            var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "";
            connectionString = $"server={dbHost};port={dbPort};database={dbName};uid={dbUser};pwd={dbPassword};";
        }
        else
        {
            connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? "server=localhost;database=ssg_system;uid=root;pwd=your_password_here;port=3306;";
        }

        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        builder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 32)));

        return new ApplicationDbContext(builder.Options);
    }
}
