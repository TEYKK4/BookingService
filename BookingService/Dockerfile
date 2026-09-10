FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BookingService/BookingService.csproj", "BookingService/"]
COPY ["RoomBooking.Contracts/RoomBooking.Contracts.csproj", "RoomBooking.Contracts/"]
RUN dotnet restore "BookingService/BookingService.csproj"
COPY . .
WORKDIR "/src/BookingService"
RUN dotnet build "./BookingService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./BookingService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BookingService.dll"]
