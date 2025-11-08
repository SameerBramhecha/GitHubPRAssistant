# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy the project file from repo root and restore
COPY GitHubPRAssistant.csproj ./
RUN dotnet restore "GitHubPRAssistant.csproj"

# Copy the rest of the repo into the image
COPY . ./

# Build
RUN dotnet build "GitHubPRAssistant.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "GitHubPRAssistant.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS="http://+:5238"

EXPOSE 5238
EXPOSE 8081

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GitHubPRAssistant.dll"]
