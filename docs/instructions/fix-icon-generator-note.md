# Fix icon generator note

This branch fixes the PowerShell icon generator after the initial app-icon PR was merged.

The previous implementation used unparenthesized arithmetic expressions inside method calls, such as `8 * $scale`. On Windows PowerShell in the GitHub Actions runner, those expressions can be parsed as object-array arguments and trigger:

```text
Method invocation failed because [System.Object[]] does not contain a method named 'op_Multiply'.
```

The updated generator:

- uses explicit `[single]` casts through a local scale helper;
- uses typed .NET constructors instead of ambiguous `New-Object` argument lists;
- keeps the same generated icon design and output path.
