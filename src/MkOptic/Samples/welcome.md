# Welcome to MkOptic

**MkOptic** is a Markdown editor and viewer with live preview.
The source text is on the left, the rendered result on the right. Switch modes with the buttons
*Source / Split / Preview* or with `Ctrl+1`, `Ctrl+2`, `Ctrl+3`.

## Text formatting

Plain text, **bold**, *italic*, ~~strikethrough~~, `inline code`,
==highlight==, H~2~O and x^2^, and a [link](https://avaloniaui.net).

> Quote: "Simplicity is the ultimate sophistication."
> — Leonardo da Vinci (attributed)

## Tables

| Library                 | Purpose                        | License  |
| :---------------------- | :----------------------------- | :------: |
| Avalonia 12             | Cross-platform UI              |   MIT    |
| SukiUI                  | Theme and UI controls          |   MIT    |
| LiveMarkdown.Avalonia   | Markdown rendering             |   MIT    |
| AvaloniaEdit + TextMate | Editor with highlighting       |   MIT    |

## Math

Inline formula: $E = mc^2$, and also $\alpha^2 + \beta^2 = \gamma^2$.

Block formula:

$$
\int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi}
$$

$$
\mathbf{A} = \begin{pmatrix} a_{11} & a_{12} \\ a_{21} & a_{22} \end{pmatrix},
\qquad
\sum_{k=1}^{n} k = \frac{n(n+1)}{2}
$$

## Lists

- Bulleted list
  - Nested item
- Another item

1. First
2. Second
3. Third

- [x] Tables
- [x] LaTeX formulas
- [ ] Your next document

## Code

```csharp
public static int Fibonacci(int n) =>
    n < 2 ? n : Fibonacci(n - 1) + Fibonacci(n - 2);
```

```json
{ "editor": "MkOptic", "markdown": true }
```

---

Shortcuts: `Ctrl+B` — bold, `Ctrl+I` — italic, `Ctrl+K` — link,
`Ctrl+M` — formula, `Ctrl+Alt+1/2/3` — heading levels, `Ctrl+F` — find, `Ctrl+H` — replace,
`Ctrl+S` — save, `Ctrl+W` — close.
