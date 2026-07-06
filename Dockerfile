FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MyMvcApp.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
RUN mkdir -p wwwroot/uploads/expenses wwwroot/uploads/avatars
ENV ASPNETCORE_URLS=http://+:5183
EXPOSE 5183
ENTRYPOINT ["dotnet", "MyMvcApp.dll"]