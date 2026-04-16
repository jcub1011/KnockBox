# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY host/KnockBox/KnockBox.csproj host/KnockBox/
COPY sdk/KnockBox.Core/KnockBox.Core.csproj sdk/KnockBox.Core/
COPY sdk/KnockBox.Platform/KnockBox.Platform.csproj sdk/KnockBox.Platform/
COPY host/KnockBox.CardCounter/KnockBox.CardCounter.csproj host/KnockBox.CardCounter/
COPY host/KnockBox.Codeword/KnockBox.Codeword.csproj host/KnockBox.Codeword/
COPY host/KnockBox.DiceSimulator/KnockBox.DiceSimulator.csproj host/KnockBox.DiceSimulator/
COPY host/KnockBox.DrawnToDress/KnockBox.DrawnToDress.csproj host/KnockBox.DrawnToDress/
COPY host/KnockBox.HiddenAgenda/KnockBox.HiddenAgenda.csproj host/KnockBox.HiddenAgenda/
COPY host/KnockBox.Operator/KnockBox.Operator.csproj host/KnockBox.Operator/
COPY host/KnockBox.TaskMaster/KnockBox.TaskMaster.csproj host/KnockBox.TaskMaster/
COPY host/Directory.Plugin.targets host/
RUN dotnet restore host/KnockBox/KnockBox.csproj

COPY . .
WORKDIR /src/host/KnockBox
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "KnockBox.dll"]