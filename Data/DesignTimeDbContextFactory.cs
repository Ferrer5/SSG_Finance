using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyMvcApp.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "server=localhost;database=ssg_system;uid=root;pwd=password;port=3306;";

        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        builder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 32)));

        return new ApplicationDbContext(builder.Options);
    }
}
