using System;

namespace HyTranslate.Core.Ollama;

public class OllamaException : Exception
{
    public const string DefaultErrorMessage = "Ollama çalışmıyor. `ollama serve` ile başlatın.";

    public OllamaException() : base(DefaultErrorMessage)
    {
    }

    public OllamaException(string message) : base(message)
    {
    }

    public OllamaException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
