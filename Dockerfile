# Build the API with the .NET SDK, then run it from the smaller ASP.NET image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MyExpenses.Api/MyExpenses.Api.csproj MyExpenses.Api/
RUN dotnet restore MyExpenses.Api/MyExpenses.Api.csproj

COPY MyExpenses.Api/ MyExpenses.Api/
RUN dotnet publish MyExpenses.Api/MyExpenses.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .

# Render supplies PORT for web services. Use 8080 locally when it is absent.
ENTRYPOINT ["sh", "-c", "dotnet MyExpenses.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]
