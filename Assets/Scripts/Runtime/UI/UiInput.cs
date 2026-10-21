namespace WFC.Runtime
{
    /// <summary>
    /// Stan okna ustawień dla kamery i skrótów klawiszowych: kursor nad oknem nie obraca kamery,
    /// a pisanie w polu tekstowym nie uruchamia skrótów (np. G).
    /// </summary>
    public static class UiInput
    {
        public static bool PointerOverUi { get; set; }

        public static bool IsTyping { get; set; }
    }
}
