# Third-party notices

## Arbiter

Runtime source under `vendor/Arbiter`, from [ewrogers/Arbiter](https://github.com/ewrogers/Arbiter), commit `3b5af118bfc3310189b42879d2bab59205af9c88`.

Copyright 2025 Erik Rogers

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Local modifications: optional working directory in `Arbiter.Interop/Process/SuspendedProcess.cs`; native bank-deposit quantity serialization/deserialization in `Arbiter.Net/Client/Messages/ClientMerchantMessage.cs`. Projects target .NET 10. The upstream MIT license is retained in `vendor/Arbiter/LICENSE`.

## da-rpc

Bank-menu protocol layout in `src/DAOrganizer.Core/BankMenu.cs` was informed by the decoder in [ewrogers/da-rpc](https://github.com/ewrogers/da-rpc), commit `afac5587cc4d3d24bd1da70535d43e563d1fcc40`. The RPC runtime is not included.

MIT License

Copyright (c) 2026 Erik Rogers

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Packages

The application also uses Avalonia, SkiaSharp, HarfBuzzSharp, Microsoft.Data.Sqlite, SQLitePCLRaw and the .NET runtime. Package license files and runtime notices are included with the published application where supplied by their packages. Test-only dependencies include xUnit, Microsoft.NET.Test.Sdk and Avalonia.Headless.

Dark Ages executable, maps and item art remain in the user's installed game; they are not distributed with this application. WorldLogs is read from the user's supplied folder and is not copied into the package.

## Vorlof item references

Selected item names and factual classifications in `ItemCategories.cs` were checked against Vorlof on September 29, 2026:

- https://vorlof.com/general/consumables.php
- https://vorlof.com/weapons.php
- https://vorlof.com/armors.php
- https://vorlof.com/equipment.php

The organizer bundles its own classification rules. It does not include Vorlof page text, item descriptions or artwork, and makes no runtime requests to the site. These references do not imply endorsement or a complete item catalog.

## Original visual assets

The Celtic seal and interlaced rules in `src/DAOrganizer.App/Ornaments.cs` are original vector drawings, covered by the repository MIT license. Item images are original Dark Ages sprites read from the user's installed game. Raw game assets are not bundled. Documentation screenshots show those sprites inside the app with fictional accounts; the depicted game artwork retains its original ownership and is not relicensed under MIT. No generated item illustrations or external fonts are included. Georgia, Segoe UI and Consolas are requested from the operating system.

Trinket names/classifications were checked against [Vorlof's trinket list](https://vorlof.com/trinkets.html). The app includes factual names and local rules, not the site's descriptions or image files.
