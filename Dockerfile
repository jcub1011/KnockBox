# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY KnockBox/KnockBox.csproj KnockBox/
COPY KnockBox.Core/KnockBox.Core.csproj KnockBox.Core/
COPY KnockBox.CardCounter/KnockBox.CardCounter.csproj KnockBox.CardCounter/
COPY KnockBox.DiceSimulator/KnockBox.DiceSimulator.csproj KnockBox.DiceSimulator/
RUN dotnet restore KnockBox/KnockBox.csproj

COPY . .
WORKDIR /src/KnockBox
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "KnockBox.dll"]