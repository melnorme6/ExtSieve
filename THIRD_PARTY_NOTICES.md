# Third-party notices

## Packaged runtime dependencies

The self-contained application package includes the following runtime dependency
families. Exact resolved versions remain recorded in `ExtSieve.App.deps.json` inside
each package.

- .NET Runtime, licensed under MIT. The packaging script includes the exact runtime
  pack's `DOTNET_LICENSE.txt` and `DOTNET_THIRD_PARTY_NOTICES.txt`.
- Avalonia UI 12.1.2, licensed under MIT.
- Avalonia ANGLE Windows natives, whose exact BSD-style `ANGLE_LICENSE.txt` is included
  in the Windows package.
- SkiaSharp and HarfBuzzSharp, licensed under MIT. Each platform package includes
  `SKIASHARP_LICENSE.txt` and the combined
  `SKIASHARP_THIRD_PARTY_NOTICES.txt` supplied with the native assets.
- MicroCom.Runtime 0.11.6, licensed under MIT.
- Tmds.DBus.Protocol 0.94.1, licensed under MIT.

The Linux package selects notices from the resolved Linux runtime and native-asset
packages. Test-only dependencies are not distributed in application packages and
remain covered by the development dependency audit.

## Tabler Icons

ExtSieve includes selected outline icon path data from Tabler Icons v3.46.0.

Source: <https://github.com/tabler/tabler-icons/tree/v3.46.0>

Copyright (c) 2020-2026 Paweł Kuna

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
