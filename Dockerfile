FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file first to cache NuGet restore layer
COPY MyMvcApp.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user to avoid running containers as root
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

COPY --from=build /app/publish .

# Prepare upload directories with correct ownership for the app user
RUN mkdir -p /app/wwwroot/uploads/avatars /app/wwwroot/uploads/expenses \
    && chown -R appuser:appgroup /app/wwwroot/uploads

USER appuser

EXPOSE 8080

ENTRYPOINT ["dotnet", "MyMvcApp.dll"]
