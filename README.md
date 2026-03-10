# 📦 RFIDTrackBin

![Platform](https://img.shields.io/badge/platform-Android-green)
![Framework](https://img.shields.io/badge/framework-Xamarin.Android-blue)
![Language](https://img.shields.io/badge/language-C%23-purple)
![RFID](https://img.shields.io/badge/RFID-Unitech%20SDK-orange)
![Database](https://img.shields.io/badge/database-SQLite-blue)
![Status](https://img.shields.io/badge/status-Production-brightgreen)

------------------------------------------------------------------------

## 📖 Overview

RFIDTrackBin is an enterprise mobile application built with
Xamarin.Android designed to manage the traceability of returnable
plastic containers using RFID technology.

------------------------------------------------------------------------

## 🎯 Main Capabilities

  Module         Description
  -------------- -----------------------------------------
  Login          Authentication using credentials or NFC
  Inventory      RFID scanning and container validation
  Entradas       Registration of inbound containers
  Salidas        Registration of container dispatch
  Verification   Operational validation and auditing

------------------------------------------------------------------------

## 🏗 System Architecture

``` mermaid
flowchart LR
Operator --> RFIDReader
RFIDReader --> MobileApp
MobileApp --> LocalDB
MobileApp --> BackendAPI
BackendAPI --> EnterpriseDB
```

------------------------------------------------------------------------

## 🧩 Technology Stack

  Layer            Technology
  ---------------- ----------------------------
  Platform         Xamarin.Android
  Language         C#
  RFID             Unitech RFID SDK
  Local Database   SQLite
  Serialization    Newtonsoft.Json
  UI               AndroidX + Google Material

------------------------------------------------------------------------

## 🗂 Project Structure

    RFIDTrackBin
    │
    ├── Activities
    ├── Fragments
    ├── Models
    ├── Helpers
    ├── Infrastructure
    └── Resources

------------------------------------------------------------------------

## License

Private enterprise software -- internal use only.
