from __future__ import annotations

import abc
import traceback


class BiometricResult:
    def __init__(self, success: bool, user_id: int | None = None, message: str = ""):
        self.success = success
        self.user_id = user_id
        self.message = message


class IBiometricProvider(abc.ABC):
    @abc.abstractmethod
    def enroll(self, user_id: int) -> BiometricResult:
        ...

    @abc.abstractmethod
    def verify(self) -> BiometricResult:
        ...


class BiometricProviderStub(IBiometricProvider):
    def enroll(self, user_id: int) -> BiometricResult:
        raise NotImplementedError("Módulo biométrico não instalado")
        return BiometricResult(False, message="Módulo biométrico não instalado")

    def verify(self) -> BiometricResult:
        raise NotImplementedError("Módulo biométrico não instalado")
        return BiometricResult(False, message="Módulo biométrico não instalado")
