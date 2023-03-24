namespace SRXDModManager.Library; 

public readonly struct Address {
    public string Owner { get; }
    
    public string Name { get; }

    public Address(string owner, string name) {
        Owner = owner;
        Name = name;
    }

    public override string ToString() => $"{Owner}/{Name}";

    public static bool TryParse(string str, out Address address) {
        if (string.IsNullOrWhiteSpace(str)) {
            address = default;

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
                address = default;

                return false;
            }

            firstSlash = i;
        }

        if (firstSlash < 0 || lastNonWhitespace <= firstSlash || firstNonWhitespace >= firstSlash) {
            address = default;

            return false;
        }

        address = new Address(
            str.Substring(firstNonWhitespace, firstSlash - firstNonWhitespace),
            str.Substring(firstSlash + 1, lastNonWhitespace - firstSlash));

        return true;
    }
}