<p align="center">
  <img src="sky-lis-logo.png" alt="Sky LIS — Laboratory Information System" width="420">
</p>

<h3 align="center">Every result, precisely placed.</h3>

<p align="center">
  Cloud-native, multi-tenant SaaS Laboratory Information System —<br>
  from specimen to signed report, with an immutable audit trail behind every step.
</p>

---

## About

**Sky LIS** is a multi-tenant SaaS Laboratory Information System covering the full clinical laboratory workflow: patient registration, order management, phlebotomy and collection, accessioning and sample tracking, results entry, technical and medical validation, and reporting and delivery — plus specialty departments (microbiology, histopathology and cytology, blood bank and transfusion, molecular diagnostics), quality control (IQC/EQA), inventory, billing and insurance, and B2B referral, patient, and physician portals.

- **Technology baseline:** .NET 10 · PostgreSQL 17
- **Interoperability:** HL7 / FHIR / Open API, instrument integration middleware
- **Requirements standard:** ISO/IEC/IEEE 29148:2018
- **Quality context:** designed for ISO 15189-accredited laboratory operations

## Repository contents

| File | Description |
|---|---|
| `SLIS-SRS-001 - Sky LIS Software Requirements Specification.docx` | Software Requirements Specification, Rev 1.0 |
| `SLIS-SRS-001 - Sky LIS Software Requirements Specification (Rev 1.1).docx` | Software Requirements Specification, Rev 1.1 (current) |
| `Sky LIS - Prototype (standalone).html` | Standalone UI prototype, v1 |
| `Sky LIS - Prototype (standalone)v2.0.html` | Standalone UI prototype, v2.0 (current) |
| `sky-lis-logo.png` | Sky LIS brand logo |

The prototypes are self-contained HTML files — open them directly in a browser, no build or server required.

## Functional modules

The SRS specifies 25 functional modules:

| # | Module | # | Module |
|---|---|---|---|
| M01 | Tenant & Subscription Administration | M14 | Molecular Diagnostics |
| M02 | User, Role & Access Management | M15 | Quality Control (IQC / EQA) |
| M03 | Master Data & System Setup | M16 | Inventory Management |
| M04 | Patient Management | M17 | Billing, Insurance & Finance |
| M05 | Order Management | M18 | B2B Referral Portal |
| M06 | Phlebotomy & Collection | M19 | Patient Portal |
| M07 | Accessioning & Sample Tracking | M20 | Physician Portal |
| M08 | Worklists & Processing | M21 | Instrument Integration Middleware |
| M09 | Results Entry & Validation | M22 | Interoperability (HL7 / FHIR / Open API) |
| M10 | Reporting & Delivery | M23 | Analytics & Dashboards |
| M11 | Microbiology | M24 | Document Control & Compliance |
| M12 | Histopathology & Cytology | M25 | Notification Center |
| M13 | Blood Bank & Transfusion Service | | |

## Brand

| Color | Hex | Use |
|---|---|---|
| Sky Blue | `#0284C7` | Primary accent |
| Navy | `#101D2C` | Headings, dark surfaces |
| Light Blue | `#E7F4FD` | Tints, backgrounds |
| Accent Blue | `#74BCE2` | Secondary accents |
| Slate | `#5A6472` | Secondary text |

---

<p align="center"><sub>National Technology · Confidential — internal and authorized development partners only</sub></p>
