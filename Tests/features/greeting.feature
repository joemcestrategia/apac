Feature: Greeting
  As a user of the Apac library
  I want to get a greeting message
  So that I feel welcomed

  Scenario: Default greeting
    Given no name is provided
     When I call the greet function
     Then it returns Hello, World!

  Scenario: Greeting with a name
    Given the name is Alice
     When I call the greet function
     Then it returns Hello, Alice!
