# ExtSieve Branding Assets

## Authoritative source assets

The files in `source/` preserve the exact approved master pixels while omitting
embedded provenance and other ancillary metadata that is not required by the product.
Original provenance-bearing inputs are retained separately and are not part of this
package.
The default application mark is `source/extsieve-app-icon-light-master.png`. The dark
variant is a distinct supported mark for dark presentation contexts.

| Source asset | Role | SHA-256 |
| --- | --- | --- |
| `source/extsieve-app-icon-light-master.png` | Default/light application mark | `EC65FD29CCDD08225949795258966D0BDB99810B8A7958033EF375BE4F1278B4` |
| `source/extsieve-app-icon-dark-master.png` | Dark-context application mark | `46898D3A76D13DFB5F21C63EAF1F2070C1C70E55E7AF973D0E2F01033743CBAA` |
| `source/extsieve-logo-horizontal-master.png` | Horizontal product logo | `61BD650035ECF1C2A0D42F5E7CCF700B1EAE0A3F8073FA49C9DB03A801811D3A` |
| `source/extsieve-readme-hero-master.png` | Repository hero image | `4AFA289E785871E008619328DB9B20B92C94C045DB81C0F1C427EF58F169D063` |

## Derived assets and usage

The PNG companions preserve the source pixels while removing ancillary metadata.

| Derived asset | Intended use |
| --- | --- |
| `docs/extsieve-logo-horizontal.png` | Wide documentation layouts |
| `docs/extsieve-logo-mark.png` | Default/light documentation mark |
| `docs/extsieve-logo-mark-dark.png` | Dark documentation mark |
| `docs/extsieve-readme-hero.png` | Main repository README hero |
| `app/extsieve-app-icon.png` | Embedded Avalonia window icon |
| `app/extsieve-app-icon-dark.png` | Supported dark-context application mark |
| `app/extsieve-app-icon.ico` | Multi-resolution Windows executable icon |
| `app/extsieve-app-icon-256.png` | 256 x 256 Linux hicolor launcher icon |

The Linux launcher icon is a metadata-stripped 256 x 256 resampling of the approved
default application mark. Its SHA-256 is
`25F012127579F7A0C045BA3CDAF3E1E91DD25075322230EB27C6932C428E8AF0`.

The application uses one stable default window and executable icon; it does not switch
the operating-system icon when the application theme changes. Platform-specific Linux
launcher integration belongs to the packaging milestone.

## Raster-source limitations

The approved masters are PNG files rather than true vector artwork. Derived sizes are
therefore resampled pixels and cannot provide editable paths, resolution-independent
geometry, or hand-tuned hinting at every small icon size. A true SVG/vector master and
purpose-tuned small icons may be added later without changing the approved identity.
