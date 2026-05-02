namespace RunFence.Launch;

public class CredentialNotFoundException(string message) : Exception(message);

public class MissingPasswordException(string message) : Exception(message);
