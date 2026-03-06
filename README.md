# RFIDTrackBin

## Overview

RFIDTrackBin is a mobile application built with **Xamarin.Android**
designed to manage the traceability of returnable plastic containers
using **RFID technology**.\
The system allows operators to register container movements, perform
inventories, validate RFID tags against a master catalog, and record
operational transactions directly from industrial Android devices.

The application integrates RFID readers, NFC, and enterprise services to
provide real‑time visibility of container flows across logistics areas.

------------------------------------------------------------------------

## Main Functional Modules

### Login

- User authentication using credentials or NFC.
- Initializes configuration and session context.

### Inventario (Inventory)

- RFID scanning of containers in a specific area.
- Validation of scanned tags against the current catalog.
- Duplicate tag filtering.
- Audible feedback for successful reads.
- Local persistence of scanned tags.

### Entradas (Inbound Containers)

- Registration of incoming returnable containers.
- Validation against supplier and operational data.
- RFID batch capture.

### Salidas (Outbound Containers)

- Registration of container dispatch.
- Traceability of containers leaving operational areas.

### Verificación

- Operational validation of RFID containers.
- Used for audit and verification processes.

------------------------------------------------------------------------

## Key Features

- RFID tag scanning using **Unitech RFID SDK**
- NFC tag support
- Duplicate RFID filtering before visualization
- Real-time tag validation against catalog
- Audible feedback on successful reads
- Local data storage using SQLite
- Integration with enterprise backend services
- Mobile UI based on Android Fragments

------------------------------------------------------------------------

## Technology Stack

  Component                Technology
  ------------------------ -------------------------------------
  Platform                 Xamarin.Android
  Language                 C#
  Target Android Version   Android 13
  RFID SDK                 Unitech RFID SDK
  Local Database           SQLite
  JSON Serialization       Newtonsoft.Json
  Device Information       Xam.Plugin.DeviceInfo
  Media Capture            Xam.Plugin.Media
  UI Libraries             AndroidX AppCompat, Google Material
  Connectivity             System.Net.Http
  Database Connectivity    MySql.Data

------------------------------------------------------------------------

## Project Architecture

The application follows a modular structure composed of:

- **Activities**
  - `LoginActivity`
  - `MainActivity`
- **Fragments**
  - `InventarioFragment`
  - `EntradasFragment`
  - `SalidasFragment`
  - `VerificacionFragment`
- **Models**
  - `TagLeido`
  - `Flete`
  - `FleteItem`
- **Helpers**
  - Bluetooth integration
  - Server time synchronization
  - Utility services
- **Infrastructure**
  - Local persistence services
  - Broadcast receivers for RFID/NFC events
  - Handler-based UI update messaging

------------------------------------------------------------------------

## Hardware Compatibility

The application is designed to operate with industrial Android handheld
devices equipped with RFID readers such as:

- Unitech handheld terminals with integrated RFID modules

------------------------------------------------------------------------

## Build Requirements

To compile the project you need:

- Visual Studio with Xamarin.Android support
- Android SDK compatible with **API Level 33**
- Access to the **Unitech RFID SDK**
- Android device with RFID capability for testing

------------------------------------------------------------------------

## External Dependencies

NuGet packages used in the project:

- Newtonsoft.Json
- sqlite-net-pcl
- MySql.Data
- Xamarin.Essentials
- Xam.Plugin.DeviceInfo
- Xam.Plugin.Media
- Xamarin.AndroidX.AppCompat
- Xamarin.Google.Android.Material

------------------------------------------------------------------------

## Project Structure (Simplified)

    RFIDTrackBin
    │
    ├── Activities
    │   ├── LoginActivity.cs
    │   └── MainActivity.cs
    │
    ├── Fragments
    │   ├── InventarioFragment.cs
    │   ├── EntradasFragment.cs
    │   ├── SalidasFragment.cs
    │   └── VerificacionFragment.cs
    │
    ├── Models
    │   ├── TagLeido.cs
    │   ├── Flete.cs
    │   └── FleteItem.cs
    │
    ├── Helpers
    │   ├── BluetoothHelper.cs
    │   └── HoraServidorService.cs
    │
    ├── Infrastructure
    │   ├── GuardaLocal.cs
    │   ├── AppLogger.cs
    │   └── Utilities.cs
    │
    └── Resources
        ├── layout
        ├── drawable
        ├── values
        └── menu

------------------------------------------------------------------------

## Operational Context

RFIDTrackBin is intended for **logistics environments where returnable
containers must be tracked across multiple operational areas** such as:

- Warehouses
- Packing facilities
- Distribution centers
- Agricultural logistics operations

------------------------------------------------------------------------

## License

Internal enterprise software -- not intended for public distribution.
