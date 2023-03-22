using System;

namespace SRXDModManager.Library; 

public readonly struct Repository {
    public string Owner { get; }
    
    public string Name { get; }

    public Repository(string owner, string name) {
        Owner = owner;
        Name = name;
    }

    public override string ToString() => $"{Owner}/{Name}";

    public static bool TryParse(string str, out Repository repository) {
        if (string.IsNullOrWhiteSpace(str)) {
            repository = default;

            return false;
        }
        
        int firstNonWhitespace = -1;
        int lastNonWhitespace = -1;
        int firstSlash = -1;

        for (int i = 0; i < str.Length; i++) {
            char c = str[i];
            
            if (!char.IsWhiteSpace(c)) {
                if (firstNonWhitespace < 0)
                    firstNonWhitespace = i;

                lastNonWhitespace = i;
            }
            
            if (c != '/')
                continue;

            if (firstSlash >= 0) {
                repository = default;

                return false;
            }

            firstSlash = i;
        }

        if (firstSlash < 0 || lastNonWhitespace <= firstSlash || firstNonWhitespace >= firstSlash) {
            repository = default;

            return false;
        }

        repository = new Repository(
            str.Substring(firstNonWhitespace, firstSlash - firstNonWhitespace),
            str.Substring(firstSlash + 1, lastNonWhitespace - firstSlash));

        return true;
    }
}