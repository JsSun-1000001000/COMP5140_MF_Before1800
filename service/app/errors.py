class ServiceError(Exception):
    """Only public, constant messages belong here; never include upstream bodies."""

    def __init__(self, status: int, code: str, message: str, *, attempts: int = 0):
        super().__init__(message)
        self.status = status
        self.code = code
        self.message = message
        self.attempts = attempts

    def body(self) -> dict:
        return {"error": {"code": self.code, "message": self.message, "attempts": self.attempts}}
