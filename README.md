# Hospitality POS Desktop

Windows desktop POS prototype for the Restrunt hospitality platform (offline-first/local-first foundation).

## Prerequisites
- Windows
- .NET SDK 10.x (`net10.0-windows` target)
- Visual Studio 2022+ with WPF/.NET desktop workload (optional, for IDE)

## Build and run
From this folder:

```powershell
dotnet build .\HospitalityPos.slnx
dotnet run --project .\src\Hospitality.Pos.Desktop\Hospitality.Pos.Desktop.csproj
```

## Local data
- SQLite database is created at: `%LocalAppData%\RestruntPos\pos.db`
- Schema and demo data are initialized on app startup (`Data/PosDatabase.cs`, `Data/DemoDataSeeder.cs`).

## Project layout
- `src/Hospitality.Pos.Desktop/` — WPF app (MVVM), views/viewmodels, services, repositories, domain, themes, local data access.
- Solution: `HospitalityPos.slnx`

## Notes
- Current state is an early local prototype (billing/tables/kitchen/menu/reports/settings UI and local data flows).
- Backend sync, SQLCipher/DPAPI hardening, device/payment integrations, installer/update, and automated tests are planned but not fully implemented yet (see parent workspace planning docs/specs).
