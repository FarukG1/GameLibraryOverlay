# Repository conventions

[Documentation index](README.md)

The README introduces the app and provides a quick start. Detailed guides live in
`docs/`, commands in `scripts/`, and .NET solution and build settings at the root.
Existing source and test project boundaries are preserved.

## Research references

These popular public projects informed the organization without copying their
text or adopting their organization-specific contribution policies:

| Reference | Pattern applied here |
| --- | --- |
| [Microsoft PowerToys](https://github.com/microsoft/PowerToys) | Clear entry point, separate developer docs, source and tool directories |
| [PowerToys contributor guide](https://github.com/microsoft/PowerToys/blob/main/CONTRIBUTING.md) | Discoverable contribution instructions |
| [Lively Wallpaper](https://github.com/lively-community/lively) | Feature-oriented README with setup, support, licensing |
| [.NET runtime coding style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md) | Written guidance and respect for surrounding file conventions |
| [GitHub README guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes) | Relative links and descriptive headings |

Comment guidance is tailored to constraints already documented in this code.
Existing source comments remain intact. Badges, release URLs, maintainer contacts,
and ownership rules can be added when actual destinations are known.
