Solution: LOCKnet
├─ src/
│  ├─ LOCKnet.Core/           <- keine Referenzen nötig
│  ├─ LOCKnet.Data/           <- Referenziert Core
│  ├─ LOCKnet.App/            <- Referenziert Core & Data
│  └─ LOCKnet.CLI/            <- Referenziert Core & Data
├─ tests/
│  ├─ LOCKnet.Core.Tests/     <- Referenziert Core
│  └─ LOCKnet.Data.Tests/     <- Referenziert Data
└─ docs/                      <- Architektur, Sicherheit, Screenshots