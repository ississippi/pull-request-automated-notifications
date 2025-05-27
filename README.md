# pull-request-automated-notifications
Websockets client chat with LLM

The Automated Pull Request Reviewer Bot leverages Large Language Models (LLMs) to  automate the review of pull requests (PRs) . The purpose is to  enhance the efficiency and accuracy of code reviews by providing detailed feedback,  identifying potential issues, and suggesting improvements to streamline the code review process.

** See pull-request-automated-review/docs for all project documentation.
** See /CloudFormation for all CF templates for this project. **  
** See /docs/Deployments.docx for notes on build and deployments of all related components including CF for the infrastructure.**  

** Repos included as part of this project: **  
pull-request-automated-review: Main project repo. Code review orchestration and github integration. Lamdas.  
pull-request-automated-chat: Chat conversation inteface. Python running in ECS.  
pull-request-automated-notifications. Push PR service. C# running in ECS.  
pull-request-automated-git-provider. Not implemented. REST API for git services. This functionality currently part of pull-request-automated-review.  
pull-request-automated-experiments: project for prototyping API interfaces.  
pull-request-test-repo: Early repo used to create PRs for testing.   



