from pytest_bdd import given, when, then, parsers, scenario
from apac import greet

FEATURES_DIR = "Tests/features"

@scenario("greeting.feature", "Default greeting", features_base_dir=FEATURES_DIR)
def test_default_greeting():
    pass

@scenario("greeting.feature", "Greeting with a name", features_base_dir=FEATURES_DIR)
def test_greeting_with_name():
    pass

@given("no name is provided", target_fixture="name")
def no_name():
    return None

@given(parsers.parse("the name is {name}"), target_fixture="name")
def given_name(name):
    return name

@when("I call the greet function", target_fixture="result")
def call_greet(name):
    if name is None:
        return greet()
    return greet(name)

@then(parsers.parse("it returns {expected}"))
def check_result(result, expected):
    assert result == expected
