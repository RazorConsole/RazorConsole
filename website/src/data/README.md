# Component Documentation Generation

This directory contains the component documentation system that automatically extracts parameter information from XML documentation files.

## How It Works

1. **XML Import** (`xml-parser.ts`)
   - Imports `RazorConsole.Core.xml` using Vite's `?raw` syntax
   - Parses XML at build-time to extract component properties
   - Extracts parameter names and descriptions from `<member>` elements

2. **Metadata & Type Overrides** (`components.generated.ts`)
   - Contains manual metadata (category, description, examples)
   - Type overrides for accurate C# type representation
   - Merges XML docs with manual data

3. **Component Export** (`components.ts`)
   - Exports the final `components` array
   - Used by the Detail page to render documentation

## Adding a New Component

To add documentation for a new component:

1. Add entry to `componentMetadata` in `components.generated.ts`:
```typescript
NewComponent: {
  category: "Display",
  description: "Your component description",
  examples: ["NewComponent_1.razor"]
}
```

2. (Optional) Add type overrides if inference is incorrect:
```typescript
typeOverrides: {
  NewComponent: {
    MyProperty: "ComplexType<T>"
  }
}
```

3. Ensure the component has XML docs in C#:
```csharp
/// <summary>
/// Property description
/// </summary>
[Parameter]
public string MyProperty { get; set; }
```

## Benefits

✅ **Always in sync** - Parameters automatically reflect C# source  
✅ **Less maintenance** - No manual parameter definitions  
✅ **Single source of truth** - XML docs drive the website  
✅ **Type safety** - TypeScript interfaces ensure consistency  

## Limitations

⚠️ **Default values** - Not extracted from XML (would need source analysis)  
⚠️ **Type inference** - Some types need manual overrides  
⚠️ **Build dependency** - Requires XML file to be generated first  

## Troubleshooting

**Error: Cannot find XML file**
- Ensure `dotnet build` has run and generated the XML doc file
- Check path in `xml-parser.ts` matches your build output

**Missing parameters**
- Verify C# properties have `/// <summary>` XML comments
- Check property matches pattern: `P:RazorConsole.Components.{Name}.{Property}`

**Wrong types displayed**
- Add type override in `components.generated.ts`
- Update `inferTypeFromProperty()` for better inference
